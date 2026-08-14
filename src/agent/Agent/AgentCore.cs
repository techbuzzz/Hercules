using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hercules.Agent.Loop;
using Hercules.Context;
using Hercules.Audit;
using Hercules.Budget;
using Hercules.Config;
using Hercules.Contracts;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Observability;
using Hercules.Mesh.Verification;
using Hercules.Quotas;
using Hercules.Skills;
using Hercules.Storage;
using Hercules.Tools;
using Hercules.Tools.Approval;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Agent;

/// <summary>
///     Per-request override для bounded execution limits.
///     Используется в <see cref="AgentCore.HandleAsync(string, BoundedExecutionOptions?, CancellationToken)" />.
/// </summary>
public sealed record BoundedExecutionOptions
{
    /// <summary>Override для MaxToolIterations. Null = использовать конфиг.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Override для wall-clock timeout (секунды). Null = использовать конфиг. 0 = unlimited.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Override для MaxRecursionDepth. Null = использовать конфиг. 0 = unlimited.</summary>
    public int? MaxRecursionDepth { get; init; }
}

/// <summary>Ответ агента на один запрос пользователя.</summary>
public sealed record AgentResponse
{
    public string Answer { get; init; } = "";
    public string Mode { get; init; } = "direct"; // skill | direct | tool
    public string Confidence { get; init; } = "medium"; // high | medium | low
    public string Provider { get; init; } = "";
    public Skill? UsedSkill { get; init; }
    public string? ToolUsed { get; init; }

    /// <summary>Если задан — агент предлагает создать навык (нужно подтверждение пользователя).</summary>
    public string? ProposeSkillForInput { get; init; }

    /// <summary>Если задан — агент предлагает улучшить навык с этим id.</summary>
    public string? ProposeImproveSkillId { get; init; }

    public string? ProposeImproveSkillName { get; init; }

    /// <summary>[task_046] Метаданные верификации. Null если верификация не выполнялась.</summary>
    public VerificationMetadata? Verification { get; init; }
}

/// <summary>
///     Ядро агента: реализует главный цикл обработки запроса —
///     загрузка памяти → маршрутизация навыка → вызов LLM → tool-execution (если LLM запросил) →
///     финальный ответ → логирование → проверка порогов создания/улучшения навыков.
/// </summary>
public sealed class AgentCore : IConfigReload
{
    private static readonly Regex ConfidenceRx =
        new(@"\[confidence:\s*(high|medium|low)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Парсим JSON-блок с action из ответа LLM: {"action": "tool_name", "arguments": {...}}</summary>
    private static readonly Regex ActionRx =
        new(@"\{\s*""action""\s*:\s*""(?<name>[^""]+)""\s*,\s*""arguments""\s*:\s*(?<args>\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\})\s*\}",
            RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ActionJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILLMClient _llm;
    private readonly ILogger<AgentCore> _logger;
    private readonly MemoryManager _memory;
    private readonly SkillRouter _router;
    private readonly EmbeddingSkillRouter? _embeddingRouter;  // null when semantic routing disabled
    private Phase2Config? _phase2Config;
    private readonly SqliteSessionStore _sessions;
    private readonly SkillManager _skills;
    private readonly ToolRegistry? _tools;
    private readonly IJsonRepairService _jsonRepair;
    private readonly ToolPolicyEngine? _policy;
    private readonly IApprovalService? _approvals;
    private readonly IGuardrailService? _guardrails;
    private readonly BudgetGuard? _budgetGuard;
    private readonly IOtelService? _otel;
    private readonly IAuditService? _auditService;
    private readonly IContextBuilder? _contextBuilder;
    private readonly IVerificationPipeline? _verificationPipeline;
    private readonly Hercules.Mesh.Escalation.IEscalationService? _escalationService;
    private readonly IQuotaService? _quotaService;
    private readonly QuotaGuard? _quotaGuard;
    private readonly ISessionStateStore _sessionStates;

    private AgentConfig _cfg;
    private Activity? _currentHandleActivity;

    public AgentCore(
        ILLMClient llm,
        SkillRouter router,
        SkillManager skills,
        MemoryManager memory,
        SqliteSessionStore sessions,
        AgentConfig cfg,
        ILogger<AgentCore> logger,
        IJsonRepairService jsonRepair,
        ISessionStateStore? sessionStates = null,
        ToolRegistry? tools = null,
        ToolPolicyEngine? policy = null,
        IApprovalService? approvals = null,
        IGuardrailService? guardrails = null,
        BudgetGuard? budgetGuard = null,
        IOtelService? otel = null,
        IAuditService? auditService = null,
        EmbeddingSkillRouter? embeddingRouter = null,
        Phase2Config? phase2Config = null,
        IContextBuilder? contextBuilder = null,
        IVerificationPipeline? verificationPipeline = null,
        Hercules.Mesh.Escalation.IEscalationService? escalationService = null,
        IQuotaService? quotaService = null,
        QuotaGuard? quotaGuard = null)
    {
        _llm = llm;
        _router = router;
        _embeddingRouter = embeddingRouter;
        _phase2Config = phase2Config;
        _skills = skills;
        _memory = memory;
        _sessions = sessions;
        _cfg = cfg;
        _logger = logger;
        _jsonRepair = jsonRepair;
        _tools = tools;
        _policy = policy;
        _approvals = approvals;
        _guardrails = guardrails;
        _budgetGuard = budgetGuard;
        _otel = otel;
        _auditService = auditService;
        _contextBuilder = contextBuilder;
        _verificationPipeline = verificationPipeline;
        _escalationService = escalationService;
        _quotaService = quotaService;
        _quotaGuard = quotaGuard;
        // task_075 H7 fix: per-session state is externalised. When the caller
        // doesn't supply a store, we lazily build a process-wide default — keeps
        // existing single-tenant callers / unit tests working without churn.
        _sessionStates = sessionStates ?? new InMemorySessionStateStore();
    }

    /// <summary>
    ///     Default session id used by CLI / single-tenant callers that don't pass a sessionId explicitly.
    ///     Stable across the agent's lifetime, but per-session mutable state is still externalised
    ///     in <see cref="ISessionStateStore" /> so that a future multi-session CLI upgrade is non-breaking.
    /// </summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    ///     Resolve a <see cref="SessionState" /> by sessionId. Falls back to the default
    ///     <see cref="SessionId" /> when <paramref name="sessionId" /> is null/empty.
    /// </summary>
    private SessionState GetState(string? sessionId) =>
        _sessionStates.GetOrCreate(string.IsNullOrWhiteSpace(sessionId) ? SessionId : sessionId);

    /// <summary>Command count for the default session (backward-compat accessor for CLI/health endpoints).</summary>
    public int CommandCount => GetState(SessionId).CommandCount;

    /// <summary>Транскрипт default-сессии (для CLI / reflection-engine).</summary>
    public IReadOnlyList<ChatTurn> Transcript => GetState(SessionId).Transcript;

    /// <summary>
    ///     Применить новую конфигурацию агента без перезагрузки.
    ///     Пороги (SkillCreationThreshold, SkillImprovementThreshold, SkillEvaluationWindow,
    ///     ReflectionEveryNCommands) и SystemPrompt обновляются сразу.
    /// </summary>
    public void Reload(AppConfig config)
    {
        _cfg = config.Agent;
        _phase2Config = config.Phase2;
    }

    /// <summary>Инициализация default-сессии: создать запись и загрузить контекст памяти.</summary>
    public void StartSession() => StartSession(SessionId);

    /// <summary>Инициализация сессии по идентификатору (для multi-session Web API).</summary>
    public void StartSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId must be non-empty", nameof(sessionId));
        }

        _sessions.StartSession(sessionId);
        var state = _sessionStates.GetOrCreate(sessionId);
        state.ContextBlock = _memory.BuildContextBlock(sessionId);
    }

    /// <summary>Обработать один запрос пользователя в default-сессии.</summary>
    public Task<AgentResponse> HandleAsync(string input, CancellationToken ct = default) =>
        HandleAsync(input, null, SessionId, ct);

    /// <summary>Обработать один запрос пользователя в default-сессии (с bounded-execution override).</summary>
    public Task<AgentResponse> HandleAsync(string input, BoundedExecutionOptions? options, CancellationToken ct) =>
        HandleAsync(input, options, SessionId, ct);

    /// <summary>Обработать один запрос в конкретной сессии (multi-session Web API).</summary>
    public async Task<AgentResponse> HandleAsync(
        string input,
        BoundedExecutionOptions? options,
        string? sessionId = null,
        CancellationToken externalCt = default)
    {
        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? SessionId : sessionId;
        var state = _sessionStates.GetOrCreate(effectiveSessionId);

        var handleSw = Stopwatch.StartNew();

        // [task_013] Start agent-handle Activity (enclosing span for the whole request)
        _currentHandleActivity = _otel?.StartActivity("AgentCore.Handle", ActivityKind.Server);
        _otel?.SetTag(_currentHandleActivity, "hercules.session_id", effectiveSessionId);
        _otel?.SetTag(_currentHandleActivity, "hercules.input_length", input.Length.ToString());

        // [task_013] Ensure activity is stopped on every exit path
        AgentResponse? result = null;
        try
        {
            result = await HandleAsyncCore(input, options, effectiveSessionId, state, externalCt, handleSw);
        }
        finally
        {
            _otel?.StopActivity(_currentHandleActivity);
        }

        return result!;
    }

    /// <summary>Inner body of HandleAsync — extracted for OTel finally-block cleanliness.</summary>
    private async Task<AgentResponse> HandleAsyncCore(
        string input,
        BoundedExecutionOptions? options,
        string sessionId,
        SessionState state,
        CancellationToken externalCt,
        Stopwatch handleSw)
    {
        state.CommandCount++;
        state.ClearToolTrace();

        // [task_027] Build context using ContextBuilder (if available)
        string contextBlock;
        if (_contextBuilder is not null && _memory is not null)
        {
            var ctxAssembly = await _contextBuilder.BuildContextAsync(input, sessionId, null, externalCt);
            contextBlock = ctxAssembly.ContextBlock;
            _logger.LogDebug("[ContextBuilder] Built context: {ItemCount} items, {Tokens} tokens, truncated={Truncated}",
                ctxAssembly.ItemCount, ctxAssembly.Budget.UsedTokens, ctxAssembly.Truncated);
        }
        else
        {
            // Legacy path: use state.ContextBlock (cached at StartSession time, task_075).
            contextBlock = state.ContextBlock;
        }

        // [task_013] Record handle call metric
        OtelMetrics.HandleCounter.Add(1);

        // [task_012] Guardrail pre-check: hard-cap violations stop immediately
        if (_guardrails is not null && _budgetGuard is not null)
        {
            var preCheck = _guardrails.CheckLimits(sessionId);
            var degradation = _budgetGuard.CheckAndGetDegradationMessage(preCheck);
            if (degradation is not null)
            {
                _logger.LogWarning("[BudgetGuard] Hard-cap pre-check failed — returning degradation response");
                _otel?.SetErrorStatus(_currentHandleActivity, "guardrail_blocked");
                handleSw.Stop();
                OtelMetrics.HandleDurationHistogram.Record(handleSw.ElapsedMilliseconds);

                // [task_049] Escalate budget guardrail violations
                if (_escalationService is not null)
                {
                    _ = _escalationService.EscalateAsync(new Hercules.Mesh.Escalation.EscalationContext
                    {
                        RequestId = sessionId,
                        AgentId = "hercules-agent",
                        SessionId = sessionId,
                        Type = Hercules.Mesh.Escalation.EscalationType.BudgetExceeded,
                        Severity = Hercules.Mesh.Escalation.EscalationSeverity.High,
                        ActionPlan = "Return budget-degradation message to user",
                        Context = $"Budget guardrail: {degradation}",
                        ToolOrIntentName = "BudgetGuard",
                        RequestedBy = "agent"
                    }, externalCt);
                }

                return new AgentResponse
                {
                    Answer = degradation,
                    Confidence = "low",
                    Mode = "guardrail_blocked",
                    Provider = ""
                };
            }
            _budgetGuard.LogSoftWarnings(preCheck);
        }

        // [task_056] Quota pre-check: block on hard quota violations
        if (_quotaService is not null && _quotaGuard is not null)
        {
            var quotaResult = _quotaService.CheckQuotas(QuotaScope.Agent, "hercules");
            var quotaDegradation = _quotaGuard.CheckAndGetDegradationMessage(quotaResult);
            if (quotaDegradation is not null)
            {
                _logger.LogWarning("[QuotaGuard] Quota pre-check failed — returning degradation response");
                _otel?.SetErrorStatus(_currentHandleActivity, "quota_blocked");
                handleSw.Stop();
                OtelMetrics.HandleDurationHistogram.Record(handleSw.ElapsedMilliseconds);

                // [task_049] Escalate quota violations
                if (_escalationService is not null)
                {
                    _ = _escalationService.EscalateAsync(new Hercules.Mesh.Escalation.EscalationContext
                    {
                        RequestId = sessionId,
                        AgentId = "hercules-agent",
                        SessionId = sessionId,
                        Type = Hercules.Mesh.Escalation.EscalationType.BudgetExceeded,
                        Severity = Hercules.Mesh.Escalation.EscalationSeverity.High,
                        ActionPlan = "Return quota-degradation message to user",
                        Context = $"Quota guard: {quotaDegradation}",
                        ToolOrIntentName = "QuotaGuard",
                        RequestedBy = "agent"
                    }, externalCt);
                }

                return new AgentResponse
                {
                    Answer = quotaDegradation,
                    Confidence = "low",
                    Mode = "quota_blocked",
                    Provider = ""
                };
            }
            _quotaGuard.LogSoftWarnings(quotaResult);

            // Begin concurrency tracking
            _quotaService.BeginConcurrency(QuotaScope.Agent, "hercules");
        }

        // Reset request counters at the start of each HandleAsync call
        _guardrails?.ResetRequestCounters(sessionId);

        // Resolve effective bounded-execution parameters
        var maxIterations = options?.MaxIterations ?? _cfg.MaxToolIterations;
        var timeoutSeconds = options?.TimeoutSeconds ?? _cfg.MaxWallClockTimeoutSeconds;
        var maxRecursion = options?.MaxRecursionDepth ?? _cfg.MaxRecursionDepth;

        TimeSpan? wallClockTimeout = timeoutSeconds > 0
            ? TimeSpan.FromSeconds(timeoutSeconds)
            : null;

        // Linked CTS: parent = wall-clock timeout, child = external cancellation
        using var lcts = new LinkedCancellationTokenSource(externalCt, wallClockTimeout);
        var ct = lcts.Token;

        var loopCtx = LoopContext.Initial(maxIterations, wallClockTimeout, maxRecursion);

        // [Loop] Step 1 — Skill routing
        _logger.LogDebug("[Loop] {Step} started (input: {InputLen} chars)", loopCtx.CurrentStep, input.Length);
        var routeSw = Stopwatch.StartNew();
        RouteResult route;

        if (_embeddingRouter is not null && _phase2Config?.SemanticRoutingEnabled == true)
        {
            // [task_022] Semantic scoring engine routing
            var semResult = await _embeddingRouter.RouteAsync(input, ct);
            route = new RouteResult(semResult.MatchedSkill, (int)(semResult.Score * 100));
        }
        else
        {
            // Legacy keyword routing
            route = _router.Route(input);
        }

        state.LastInput = input;
        var systemPrompt = BuildSystemPrompt(route.MatchedSkill, contextBlock);
        routeSw.Stop();

        // [task_013] Record skill route activity and metric
        using Activity? routeActivity = _otel?.StartActivity("AgentCore.SkillRoute", _currentHandleActivity?.Context ?? default, ActivityKind.Internal);
        _otel?.SetTag(routeActivity, "skill.name", route.MatchedSkill?.Meta.Name ?? "(direct)");
        _otel?.SetTag(routeActivity, "skill.id", route.MatchedSkill?.Meta.Id ?? "");
        if (route.IsSkill)
        {
            OtelMetrics.SkillHitCounter.Add(1,
                new KeyValuePair<string, object?>("skill.id", route.MatchedSkill!.Meta.Id),
                new KeyValuePair<string, object?>("skill.name", route.MatchedSkill.Meta.Name));
        }
        _otel?.StopActivity(routeActivity);

        _logger.LogDebug("[Loop] {Step} finished in {ElapsedMs}ms — skill={SkillName}",
            LoopStep.SkillRoute, routeSw.ElapsedMilliseconds, route.MatchedSkill?.Meta.Name ?? "(direct)");

        // [Loop] Step 2 — LLM call (with tool iteration)
        var messages = BuildMessages(systemPrompt, input, state);
        loopCtx = loopCtx with { CurrentStep = LoopStep.LlmCall };
        _logger.LogDebug("[Loop] {Step} started", loopCtx.CurrentStep);
        LlmResponse llmResp;
        string toolUsed = "";
        try
        {
            (llmResp, toolUsed, loopCtx) = await RunWithToolsAsync(messages, loopCtx, route.MatchedSkill?.Meta.Id, state, ct);
        }
        catch (OperationCanceledException) when (lcts.IsWallClockTimeout)
        {
            _logger.LogWarning("[Loop] Wall-clock timeout reached after {Timeout}s — returning graceful degradation",
                timeoutSeconds);
            // [task_012] Record elapsed time for guardrails
            _guardrails?.RecordElapsedTime(sessionId, wallClockTimeout is not null ? (long)wallClockTimeout.Value.TotalMilliseconds : 0);
            _otel?.SetErrorStatus(_currentHandleActivity, "timeout");
            handleSw.Stop();
            OtelMetrics.HandleDurationHistogram.Record(handleSw.ElapsedMilliseconds);
            _ = AuditHandleResultAsync(input, "timeout", toolUsed, route.MatchedSkill?.Meta.Id, sessionId);
            return new AgentResponse
            {
                Answer = "Запрос превысил максимальное время выполнения. Попробуйте упростить запрос или увеличить лимит.",
                Confidence = "low",
                Mode = "timeout",
                UsedSkill = route.MatchedSkill
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Loop] {Step} failed: {Error}", LoopStep.LlmCall, ex.Message);
            _otel?.SetErrorStatus(_currentHandleActivity, ex.Message);
            handleSw.Stop();
            OtelMetrics.HandleDurationHistogram.Record(handleSw.ElapsedMilliseconds);
            _ = AuditHandleResultAsync(input, "error", toolUsed, route.MatchedSkill?.Meta.Id, ex.Message, sessionId);
            return new AgentResponse
            {
                Answer = $"Ошибка обращения к LLM: {ex.Message}",
                Confidence = "low",
                Mode = route.IsSkill ? "skill" : "direct",
                UsedSkill = route.MatchedSkill
            };
        }

        var (answer, confidence) = ExtractConfidence(llmResp.Text);

        // [task_049] Escalate low-confidence responses
        if (_escalationService is not null && confidence.Equals("low", StringComparison.OrdinalIgnoreCase))
        {
            _ = _escalationService.EscalateAsync(new Hercules.Mesh.Escalation.EscalationContext
            {
                RequestId = sessionId,
                AgentId = "hercules-agent",
                SessionId = sessionId,
                Type = Hercules.Mesh.Escalation.EscalationType.LowConfidence,
                Severity = Hercules.Mesh.Escalation.EscalationSeverity.Medium,
                ActionPlan = $"Return low-confidence response to user (answer: {(answer.Length > 80 ? answer[..80] + "..." : answer)})",
                Context = $"Low-confidence response ({confidence}): {answer}",
                ToolOrIntentName = route.MatchedSkill?.Meta.Id ?? "(direct)",
                RequestedBy = "agent"
            }, externalCt);
        }

        // [task_012] Record LLM usage for budget guardrails
        if (_guardrails is not null)
        {
            var tokensUsed = llmResp.InputTokens + llmResp.OutputTokens;
            var cost = EstimateCost(tokensUsed, llmResp.Provider);
            _guardrails.RecordLlmUsage(sessionId, llmResp.InputTokens, llmResp.OutputTokens, cost, llmResp.Provider);
        }

        // [task_056] Record quota usage (tokens, messages)
        if (_quotaService is not null)
        {
            var totalTokens = llmResp.InputTokens + llmResp.OutputTokens;
            _quotaService.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.TokensPerDayPerAgent, totalTokens);
            _quotaService.RecordUsage(QuotaScope.Agent, "hercules", QuotaLimitType.MessagesPerDayPerAgent, 1);
        }

        // [task_027] Compress tool trace into episodic memory if threshold reached
        if (_contextBuilder is not null && state.ToolTrace.Count > 0)
        {
            var traceSnapshot = state.ToolTrace;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _contextBuilder.CompressTraceAsync(traceSnapshot, sessionId, CancellationToken.None);
                }
                catch
                {
                    // Best-effort compression
                }
            }, CancellationToken.None);
        }

        // [Loop] Step 3 — Transcript update
        state.AppendTranscript(new ChatTurn(ChatRole.User, input));
        state.AppendTranscript(new ChatTurn(ChatRole.Assistant, answer));

        var mode = !string.IsNullOrEmpty(toolUsed)
            ? "tool"
            : route.IsSkill ? "skill" : "direct";

        // [Loop] Step 4 — Interaction log
        var logCtx = loopCtx with { CurrentStep = LoopStep.LogInteraction };
        _sessions.LogInteraction(new InteractionLog(
            sessionId, input, answer, confidence, mode,
            route.MatchedSkill?.Meta.Id, llmResp.Provider, DateTime.UtcNow));

        // Запись использования навыка (успех = уверенность не low)
        // [task_022] LatencyScorer: записываем elapsed time от начала HandleAsync
        if (route.IsSkill)
        {
            _skills.RecordUsage(route.MatchedSkill!.Meta.Id, confidence != "low", confidence, (int)handleSw.ElapsedMilliseconds);
        }

        // [task_013] Record final handle duration
        _otel?.SetTag(_currentHandleActivity, "hercules.mode", mode);
        _otel?.SetTag(_currentHandleActivity, "hercules.confidence", confidence);
        handleSw.Stop();
        OtelMetrics.HandleDurationHistogram.Record(handleSw.ElapsedMilliseconds);

        // 5-6. Пороги (skill creation/improvement)
        var response = BuildResponse(answer, confidence, llmResp.Provider, route.MatchedSkill, toolUsed, mode, state);

        // [task_046] Verification pipeline: check response before returning
        if (_verificationPipeline is not null && _verificationPipeline.Config.Enabled)
        {
            var skipConfidence = _verificationPipeline.Config.MinConfidenceToSkipVerification.ToLowerInvariant() switch
            {
                "high" => 3, "medium" => 2, "low" => 1, _ => 3
            };
            var respConfidence = confidence.ToLowerInvariant() switch
            {
                "high" => 3, "medium" => 2, "low" => 1, _ => 2
            };
            if (respConfidence < skipConfidence)
            {
                var verifyCtx = new VerificationContext
                {
                    VerificationId = "",
                    RequestId = sessionId,
                    AgentId = "hercules-agent",
                    SessionId = sessionId,
                    ResponseText = answer,
                    Mode = mode,
                    ToolUsed = string.IsNullOrEmpty(toolUsed) ? null : toolUsed,
                    Confidence = confidence,
                    Provider = llmResp.Provider
                };

                using var verifyTimeout = new CancellationTokenSource(_verificationPipeline.Config.MaxVerificationTimeMs);
                var verifyResult = await _verificationPipeline.VerifyAsync(verifyCtx, verifyTimeout.Token);

                if (verifyResult.Blocked)
                {
                    _logger.LogWarning(
                        "[VerificationPipeline] Response BLOCKED (severity={Severity}, reason={Reason})",
                        verifyResult.MaxSeverity, verifyResult.BlockingReason);
                    _otel?.SetTag(_currentHandleActivity, "hercules.verification_blocked", "true");
                    _otel?.SetTag(_currentHandleActivity, "hercules.verification_severity", verifyResult.MaxSeverity.ToString());

                    // [task_049] Escalate verification policy denials
                    if (_escalationService is not null)
                    {
                        var sev = verifyResult.MaxSeverity switch
                        {
                            Hercules.Mesh.Verification.VerificationSeverity.Critical => Hercules.Mesh.Escalation.EscalationSeverity.Critical,
                            Hercules.Mesh.Verification.VerificationSeverity.High => Hercules.Mesh.Escalation.EscalationSeverity.High,
                            Hercules.Mesh.Verification.VerificationSeverity.Medium => Hercules.Mesh.Escalation.EscalationSeverity.Medium,
                            _ => Hercules.Mesh.Escalation.EscalationSeverity.Low
                        };
                        _ = _escalationService.EscalateAsync(new Hercules.Mesh.Escalation.EscalationContext
                        {
                            RequestId = sessionId,
                            AgentId = "hercules-agent",
                            SessionId = sessionId,
                            Type = Hercules.Mesh.Escalation.EscalationType.PolicyDenial,
                            Severity = sev,
                            ActionPlan = "Block verification-blocked response and notify operator",
                            Context = $"Verification blocked: severity={verifyResult.MaxSeverity}, reason={verifyResult.BlockingReason}",
                            ToolOrIntentName = response.ToolUsed,
                            RequestedBy = "verification-pipeline"
                        }, externalCt);
                    }

                    // Return a safe degradation response instead of the original
                    return new AgentResponse
                    {
                        Answer = "Ответ заблокирован системой верификации из-за нарушения политики безопасности. Обратитесь к администратору.",
                        Mode = "verification_blocked",
                        Confidence = "low",
                        Provider = "",
                        UsedSkill = response.UsedSkill,
                        ToolUsed = response.ToolUsed,
                        ProposeSkillForInput = response.ProposeSkillForInput,
                        ProposeImproveSkillId = response.ProposeImproveSkillId,
                        ProposeImproveSkillName = response.ProposeImproveSkillName,
                        Verification = new VerificationMetadata(
                            verifyResult.VerificationId,
                            false,
                            verifyResult.MaxSeverity.ToString(),
                            verifyResult.BlockingReason,
                            verifyResult.ElapsedMs)
                    };
                }

                _otel?.SetTag(_currentHandleActivity, "hercules.verification_passed", "true");
                _logger.LogDebug("[VerificationPipeline] Response passed verification ({ElapsedMs}ms)",
                    verifyResult.ElapsedMs);

                response = new AgentResponse
                {
                    Answer = response.Answer,
                    Mode = response.Mode,
                    Confidence = response.Confidence,
                    Provider = response.Provider,
                    UsedSkill = response.UsedSkill,
                    ToolUsed = response.ToolUsed,
                    ProposeSkillForInput = response.ProposeSkillForInput,
                    ProposeImproveSkillId = response.ProposeImproveSkillId,
                    ProposeImproveSkillName = response.ProposeImproveSkillName,
                    Verification = new VerificationMetadata(
                        verifyResult.VerificationId,
                        true,
                        verifyResult.MaxSeverity.ToString(),
                        null,
                        verifyResult.ElapsedMs)
                };
            }
        }

        // [task_056] End concurrency tracking for quota
        if (_quotaService is not null)
        {
            _quotaService.EndConcurrency(QuotaScope.Agent, "hercules");
        }

        return response;
    }

    /// <summary>
    ///     Вызвать LLM с возможной tool-итерацией: если LLM вернул JSON с action,
    ///     выполнить tool, положить результат в transcript, вызвать LLM снова.
    ///     Max MaxToolIterations итераций (защита от infinite loops).
    ///     Контекст обновляется после каждого tool execution для observability.
    /// </summary>
    private async Task<(LlmResponse Response, string ToolUsed, LoopContext Context)> RunWithToolsAsync(
        List<ChatTurn> messages, LoopContext ctx, string? skillId, SessionState state, CancellationToken ct)
    {
        var sessionId = state.SessionId;
        var toolUsed = "";
        LlmResponse last = default!;
        var maxIter = ctx.MaxIterations;
        for (var iter = 0; iter <= maxIter; iter++)
        {
            last = await _llm.CompleteAsync(messages, ct);

            if (_tools is null || _tools.Names.Count == 0)
            {
                _logger.LogDebug("[Loop] {Step} finished (no tools configured)", LoopStep.LlmCall);
                return (last, toolUsed, ctx);
            }

            // Try to parse tool action from LLM output
            (string Name, string ArgsJson)? action = TryParseAction(last.Text);
            if (action is null)
            {
                _logger.LogDebug("[Loop] {Step} finished — no tool action parsed", LoopStep.LlmCall);
                return (last, toolUsed, ctx);
            }

            var (toolName, argsJson) = action.Value;
            ITool? tool = _tools.Get(toolName);
            if (tool is null)
            {
                messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                messages.Add(new ChatTurn(ChatRole.User,
                    $"[system] Tool '{toolName}' not found. Available tools: {string.Join(", ", _tools.Names)}. " +
                    "Provide a final answer (without 'action' JSON) or use a different tool."));
                continue;
            }

            // [Policy] Check tool execution policy before running
            if (_policy is not null)
            {
                var policyResult = _policy.Evaluate(new PolicyContext
                {
                    ToolName = toolName,
                    ArgumentsJson = argsJson,
                    SessionId = sessionId,
                    SkillId = skillId
                });

                if (policyResult.IsDenied)
                {
                    _logger.LogWarning("[Policy] Tool '{Name}' BLOCKED — {Reason}", toolName, policyResult.DeniedReason);
                    messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                    messages.Add(new ChatTurn(ChatRole.System,
                        $"[system] Tool '{toolName}' is blocked by policy: {policyResult.DeniedReason}. " +
                        "Provide a final answer without using this tool."));
                    continue;
                }

                if (policyResult.RequiresApproval)
                {
                    _logger.LogWarning("[Policy] Tool '{Name}' requires approval — {Reason}", toolName, policyResult.DeniedReason);

                    // task_010: check if the tool was already approved
                    if (_approvals is not null && _approvals.IsApproved(toolName, sessionId))
                    {
                        _logger.LogInformation("[Approval] Tool '{Name}' was pre-approved — proceeding with execution", toolName);
                    }
                    else
                    {
                        // Not yet approved — return waiting message
                        messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                        messages.Add(new ChatTurn(ChatRole.System,
                            $"[system] Tool '{toolName}' requires human approval: {policyResult.DeniedReason}. " +
                            "Please confirm the action in the approval interface (POST /api/approvals/{{id}}/approve) and retry. " +
                            "For now, provide a final answer without this tool."));
                        continue;
                    }
                }
            }

            // Execute tool — log step
            toolUsed = toolName;
            ctx = ctx.AfterTool(toolName);

            // [task_012] Record tool call for budget guardrails
            _guardrails?.RecordToolCall(sessionId);

            // [task_013] Trace tool execution
            var toolSw = System.Diagnostics.Stopwatch.StartNew();
            using Activity? toolActivity = _otel?.StartActivity($"Tool.{toolName}", _currentHandleActivity?.Context ?? default, ActivityKind.Internal);
            _otel?.SetTag(toolActivity, "tool.name", toolName);
            _otel?.SetTag(toolActivity, "tool.iteration", iter.ToString());

            _logger.LogDebug("[Loop] {Step} started — tool={ToolName} iter={Iter}",
                LoopStep.ToolExecute, toolName, iter);

            // Check policy cancellation before executing
            if (ctx.IsWallClockExpired)
            {
                _logger.LogWarning("[Loop] {Step} — wall-clock timeout reached at iter={Iter}", LoopStep.ToolExecute, iter);
                _otel?.SetErrorStatus(toolActivity, "wall_clock_timeout");
                toolSw.Stop();
                OtelMetrics.ToolCallDurationHistogram.Record(toolSw.ElapsedMilliseconds);
                OtelMetrics.ToolCallCounter.Add(1,
                    new KeyValuePair<string, object?>("tool", toolName),
                    new KeyValuePair<string, object?>("status", "timeout"));
                _otel?.StopActivity(toolActivity, System.Diagnostics.ActivityStatusCode.Error);
                messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                messages.Add(new ChatTurn(ChatRole.System,
                    "[system] Execution timeout reached. Provide a final answer now."));
                break;
            }

            if (ctx.CancellationRequested)
            {
                _logger.LogWarning("[Loop] {Step} — cancelled by policy before execution", LoopStep.ToolExecute);
                _otel?.SetErrorStatus(toolActivity, "cancelled_by_policy");
                toolSw.Stop();
                OtelMetrics.ToolCallDurationHistogram.Record(toolSw.ElapsedMilliseconds);
                OtelMetrics.ToolCallCounter.Add(1,
                    new KeyValuePair<string, object?>("tool", toolName),
                    new KeyValuePair<string, object?>("status", "cancelled"));
                _otel?.StopActivity(toolActivity, System.Diagnostics.ActivityStatusCode.Error);
                messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
                messages.Add(new ChatTurn(ChatRole.System,
                    "[system] Execution cancelled by policy. Provide a final answer now."));
                break;
            }

            ToolResult toolResult;
            try
            {
                toolResult = await tool.ExecuteAsync(argsJson, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Loop] Tool '{Name}' threw: {Error}", toolName, ex.Message);
                _otel?.SetErrorStatus(toolActivity, ex.Message);
                toolSw.Stop();
                OtelMetrics.ToolCallDurationHistogram.Record(toolSw.ElapsedMilliseconds);
                OtelMetrics.ToolCallCounter.Add(1,
                    new KeyValuePair<string, object?>("tool", toolName),
                    new KeyValuePair<string, object?>("status", "error"));
                _otel?.StopActivity(toolActivity, System.Diagnostics.ActivityStatusCode.Error);
                throw;
            }

            toolSw.Stop();

            // [task_027] Record tool trace entry for compression (per-session, task_075)
            state.AppendToolTrace(new ToolTraceEntry(
                toolName,
                argsJson,
                toolResult.Output,
                toolSw.ElapsedMilliseconds,
                DateTime.UtcNow,
                toolResult.Success));

            OtelMetrics.ToolCallCounter.Add(1,
                new KeyValuePair<string, object?>("tool", toolName),
                new KeyValuePair<string, object?>("status", toolResult.Success ? "ok" : "fail"));
            OtelMetrics.ToolCallDurationHistogram.Record(toolSw.ElapsedMilliseconds);
            _otel?.SetTag(toolActivity, "tool.success", toolResult.Success.ToString());
            _otel?.StopActivity(toolActivity);
            var resultContract = new ToolResultContract
            {
                Success = toolResult.Success,
                Output = toolResult.Output,
                Error = toolResult.Error,
                Metadata = toolResult.Metadata is { } meta
                    ? new Dictionary<string, object?>(meta.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)))
                    : null
            };
            var resultJson = JsonSerializer.Serialize(resultContract, ActionJsonOpts);

            _logger.LogWarning("Tool '{ToolName}' → {Status} (output: {OutputLen} chars, error: {Error_len} chars)", toolName, toolResult.Success ? "ok" : "FAIL", toolResult.Output.Length, toolResult.Error?.Length ?? 0);

            messages.Add(new ChatTurn(ChatRole.Assistant, last.Text));
            messages.Add(new ChatTurn(ChatRole.User,
                $"[system] Tool '{toolName}' result:\n```json\n{resultJson}\n```\n\n" +
                "Use this result to provide a final answer to the user. " +
                "If you need another tool call, include 'action' JSON again. " +
                "Otherwise provide a plain-text response (no JSON)."));

            if (iter == maxIter)
            {
                messages.Add(new ChatTurn(ChatRole.System,
                    "[system] Maximum tool iterations reached. Provide final answer now."));
                _logger.LogWarning("[Loop] {Step} — max iterations ({Max}) reached, stopping tool loop",
                    LoopStep.ToolExecute, maxIter);
            }
        }

        return (last, toolUsed, ctx);
    }

    private (string Name, string ArgsJson)? TryParseAction(string llmText)
    {
        // Try typed contract first (preferred path)
        ToolCallContract? contract = _jsonRepair.TryParse<ToolCallContract>(llmText);
        if (contract is not null && !string.IsNullOrWhiteSpace(contract.Action))
        {
            string argsJson;
            if (contract.Arguments.Count > 0)
            {
                argsJson = JsonSerializer.Serialize(contract.Arguments, ActionJsonOpts);
            }
            else
            {
                argsJson = "{}";
            }

            _logger.LogDebug("[Loop] TryParseAction: typed contract parsed — action={Action}", contract.Action);
            return (contract.Action, argsJson);
        }

        // Fallback: legacy regex (backward compat with older prompts or external callers)
        var cleaned = llmText;
        Match jsonMatch = Regex.Match(cleaned, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            cleaned = jsonMatch.Groups[1].Value;
        }

        Match match = ActionRx.Match(cleaned);
        if (!match.Success)
        {
            return null;
        }

        _logger.LogDebug("[Loop] TryParseAction: legacy regex fallback — action={Action}", match.Groups["name"].Value);
        return (match.Groups["name"].Value.Trim(), match.Groups["args"].Value.Trim());
    }

    private AgentResponse BuildResponse(
        string answer, string confidence, string provider, Skill? usedSkill,
        string toolUsed, string mode, SessionState state)
    {
        // 5. Порог создания навыка: если однотипный запрос повторился >= SkillCreationThreshold раз,
        //    и для него ещё нет навыка (direct-режим) — предложить создать навык.
        string? proposeSkill = null;
        if (usedSkill is null && _cfg.SkillCreationThreshold > 0)
        {
            var repeatCount = _sessions.IncrementRequestCount(SkillRouter.Normalize(state.LastInput));
            if (repeatCount >= _cfg.SkillCreationThreshold)
            {
                proposeSkill = state.LastInput;
            }
        }

        // 6. Порог улучшения навыка: если использован навык и его success_rate
        //    упал ниже SkillImprovementThreshold — предложить улучшение.
        string? proposeImproveId = null;
        string? proposeImproveName = null;
        if (usedSkill is not null &&
            usedSkill.Meta.TotalUses >= _cfg.SkillEvaluationWindow &&
            usedSkill.Meta.SuccessRate < _cfg.SkillImprovementThreshold)
        {
            proposeImproveId = usedSkill.Meta.Id;
            proposeImproveName = usedSkill.Meta.Name;
        }

        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = provider,
            UsedSkill = usedSkill,
            ToolUsed = string.IsNullOrEmpty(toolUsed)
                ? null
                : toolUsed,
            ProposeSkillForInput = proposeSkill,
            ProposeImproveSkillId = proposeImproveId,
            ProposeImproveSkillName = proposeImproveName
        };
    }

    /// <summary>
    ///     Псевдоним для <see cref="HandleAsync" /> — используется Web API адаптером.
    /// </summary>
    public Task<AgentResponse> ProcessMessageAsync(string input, CancellationToken ct = default) =>
        HandleAsync(input, null, SessionId, ct);

    /// <summary>
    ///     Оценить навык на конкретном запросе: принудительно использует skill,
    ///     игнорируя роутер. Используется SkillEvaluationEngine для запуска test suite.
    /// </summary>
    /// <param name="skill">Навык для принудительного использования.</param>
    /// <param name="input">Тестовый запрос.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>AgentResponse от навыка.</returns>
    public async Task<AgentResponse> EvaluateSkillAsync(Skill skill, string input, CancellationToken ct = default)
    {
        _logger.LogDebug("[Eval] Evaluating skill '{Name}' with input: {Input}", skill.Meta.Name, input);
        var state = GetState(SessionId);
        state.CommandCount++;

        // [task_027] Build context for eval
        string ctxBlock;
        if (_contextBuilder is not null && _memory is not null)
        {
            var assembly = await _contextBuilder.BuildContextAsync(input, state.SessionId, skill, ct);
            ctxBlock = assembly.ContextBlock;
        }
        else
        {
            ctxBlock = state.ContextBlock;
        }

        // Строим system prompt напрямую с навыком (минуя роутер)
        var systemPrompt = BuildSystemPrompt(skill, ctxBlock);
        var messages = BuildMessages(systemPrompt, input, state);

        LlmResponse llmResp;
        string toolUsed = "";
        try
        {
            (llmResp, toolUsed, _) = await RunWithToolsAsync(
                messages,
                LoopContext.Initial(_cfg.MaxToolIterations, _cfg.MaxWallClockTimeoutSeconds > 0 ? TimeSpan.FromSeconds(_cfg.MaxWallClockTimeoutSeconds) : null, _cfg.MaxRecursionDepth),
                skill.Meta.Id,
                state,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Eval] LLM call failed for skill '{Name}'", skill.Meta.Name);
            return new AgentResponse
            {
                Answer = $"Ошибка LLM: {ex.Message}",
                Confidence = "low",
                Mode = "skill",
                UsedSkill = skill
            };
        }

        var (answer, confidence) = ExtractConfidence(llmResp.Text);
        var mode = !string.IsNullOrEmpty(toolUsed) ? "tool" : "skill";

        return new AgentResponse
        {
            Answer = answer,
            Mode = mode,
            Confidence = confidence,
            Provider = llmResp.Provider,
            UsedSkill = skill,
            ToolUsed = string.IsNullOrEmpty(toolUsed) ? null : toolUsed
        };
    }

    /// <summary>Сбросить счётчик повторов запроса (после создания навыка).</summary>
    public void ResetRequestCounter(string input)
    {
        _sessions.ResetRequestCount(SkillRouter.Normalize(input));
    }

    /// <summary>Нужно ли запустить рефлексию по числу команд.</summary>
    public bool ShouldReflectByCount()
    {
        return _cfg.ReflectionEveryNCommands > 0 &&
               CommandCount > 0 &&
               CommandCount % _cfg.ReflectionEveryNCommands == 0;
    }

    /// <summary>
    ///     Завершить сессию: записать транскрипт в память и закрыть сессионное хранилище.
    /// </summary>
    public async Task EndSessionAsync(CancellationToken ct = default)
    {
        var state = GetState(SessionId);
        // [Loop] Memory update — сохраняем итоги сессии
        _logger.LogDebug("[Loop] {Step} started — session={SessionId}", LoopStep.MemoryUpdate, state.SessionId);
        await _memory.PersistSessionAsync(state.Transcript, state.SessionId, ct);
        _sessions.EndSession(state.SessionId);
        _logger.LogDebug("[Loop] {Step} finished", LoopStep.MemoryUpdate);
    }

    /// <summary>Завершить сессию синхронно (без сохранения памяти — для совместимости).</summary>
    public void EndSession()
    {
        _sessions.EndSession(SessionId);
    }

    // ---- Вспомогательные методы ----

    private string BuildSystemPrompt(Skill? skill, string contextBlock)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_cfg.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine(contextBlock);
        if (skill is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"=== АКТИВНЫЙ НАВЫК: {skill.Meta.Name} ===");
            sb.AppendLine(skill.Prompt);
        }

        // Stage 4: tool ecosystem injection
        if (_tools is not null && _tools.Names.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(_tools.ListForLLM());
        }

        sb.AppendLine();
        sb.AppendLine("В КОНЦЕ ответа добавь на отдельной строке маркер уверенности в формате: [confidence: high|medium|low]");
        return sb.ToString();
    }

    private List<ChatTurn> BuildMessages(string systemPrompt, string input, SessionState state)
    {
        var snapshot = state.TakeLastTranscript(8);

        var msgs = new List<ChatTurn> { new(ChatRole.System, systemPrompt) };
        msgs.AddRange(snapshot);
        msgs.Add(new ChatTurn(ChatRole.User, input));
        return msgs;
    }

    /// <summary>Извлечь маркер уверенности и убрать его из текста ответа.</summary>
    private static (string Answer, string Confidence) ExtractConfidence(string text)
    {
        Match match = ConfidenceRx.Match(text);
        if (!match.Success)
        {
            return (text.Trim(), "medium");
        }

        var confidence = match.Groups[1].Value.ToLowerInvariant();
        var cleaned = ConfidenceRx.Replace(text, "").Trim();
        return (cleaned, confidence);
    }

    /// <summary>
    ///     Estimate USD cost based on token count and provider.
    ///     Uses rough per-1K-token pricing for known providers.
    /// </summary>
    private static decimal EstimateCost(int totalTokens, string provider) =>
        provider.ToLowerInvariant() switch
        {
            "yandexgpt" => totalTokens * 0.0000015m,      // ~$1.50/1M tokens
            "ollama-cloud" => totalTokens * 0.000002m,      // ~$2.00/1M tokens
            "ollama-local" => 0m,                           // free
            "openai-compatible" => totalTokens * 0.0000015m,
            _ => totalTokens * 0.000002m,                   // default estimate
        };

    /// <summary>
    ///     Fire-and-forget audit logging of handle result (task_014).
    ///     Failures are swallowed to prevent handle logic from being affected by audit failures.
    /// </summary>
    private async Task AuditHandleResultAsync(
        string input,
        string result,
        string? toolUsed,
        string? skillId,
        string? error = null,
        string? sessionId = null)
    {
        if (_auditService is null) return;
        try
        {
            var details = error is not null
                ? $"error={error}"
                : $"tool={toolUsed ?? "(none)"}";

            await _auditService.LogAsync(
                actor: "agent",
                action: "handle_completed",
                target: skillId,
                details: details,
                sessionId: sessionId ?? SessionId,
                result: result);
        }
        catch
        {
            // Swallow — audit failures must not affect agent operation
        }
    }
}
