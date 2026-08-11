using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hercules.Agent;
using Hercules.LLM;

namespace Hercules.Mesh;

/// <summary>
///     Стратегия выбора лучшего ответа при fan-out.
/// </summary>
public enum FanOutStrategy
{
    /// <summary>Первый успешный ответ (fastest wins). Без LLM-judge.</summary>
    FirstSuccess,

    /// <summary>Высшая уверенность wins (без LLM-judge).</summary>
    HighestConfidence,

    /// <summary>LLM-judge выбирает лучший ответ из всех полученных.</summary>
    LlmJudge
}

/// <summary>
///     Результат fan-out запроса: несколько ответов от peer-агентов + выбранный лучший.
/// </summary>
public sealed class FanOutResult
{
    public IntentResponse? Winner { get; set; }
    public List<IntentResponse> AllResponses { get; set; } = new();
    public string SelectionMethod { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public string? JudgeRationale { get; set; }

    public bool HasWinner => Winner is not null && Winner.IsSuccess;
}

/// <summary>
///     Mesh Router — оркестратор mesh: расширенная маршрутизация с fan-out/fan-in.
///     В отличие от IntentRouter (single-peer), MeshRouter может:
///     - Отправить запрос нескольким peer'ам параллельно (fan-out).
///     - Собрать все ответы и выбрать лучший (fan-in) через LLM-judge, голосование илиHighestConfidence.
///     - Retry с экспоненциальной задержкой + CircuitBreaker для упавших peer'ов.
///     Спецификация: docs/ROADMAP-RU.md Phase 4 #17-18.
/// </summary>
public sealed class MeshRouter
{
    private readonly AgentCore _agent;
    private readonly CircuitBreaker _breaker;
    private readonly ILLMClient _llm;
    private readonly AgentManifestService _manifestService;
    private readonly CapabilityRegistry _registry;
    private readonly RetryPolicy _retry;
    private readonly IntentTransport _transport;

    public MeshRouter(
        AgentCore agent,
        CapabilityRegistry registry,
        IntentTransport transport,
        AgentManifestService manifestService,
        ILLMClient llm,
        CircuitBreaker breaker,
        RetryPolicy retry)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _breaker = breaker ?? throw new ArgumentNullException(nameof(breaker));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
    }

    /// <summary>Стратегия выбора лучшего ответа при fan-out.</summary>
    public FanOutStrategy Strategy { get; set; } = FanOutStrategy.HighestConfidence;

    /// <summary>Максимум параллельных peer-вызовов при fan-out.</summary>
    public int MaxParallelPeers { get; set; } = 5;

    /// <summary>Минимум peer-ответов для fan-out (если меньше — запрос отправляется одному peer'у).</summary>
    public int MinPeersForFanOut { get; set; } = 2;

    /// <summary>Таймаут ожидания всех ответов при fan-out (мс).</summary>
    public int FanOutTimeoutMs { get; set; } = 30_000;

    /// <summary>
    ///     Маршрутизировать intent с поддержкой fan-out:
    ///     1. Найти всех peer'ов с подходящей capability.
    ///     2. Если peer'ов >= MinPeersForFanOut — fan-out (параллельно), затем выбрать лучший.
    ///     3. Если peer'ов
    ///     < MinPeersForFanOut — отправить одному ( как IntentRouter).
    ///         4. Если peer'ов нет — обработать локально.
    /// 
    /// </summary>
    public async Task<FanOutResult> RouteWithFanOutAsync(IntentEnvelope envelope, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(envelope);

        var ownAgentId = _manifestService.Current.AgentId;

        // 1. Найти всех peer'ов с подходящей capability (исключая себя)
        var peers = _registry.FindByCapability(envelope.Intent)
            .Where(p => !p.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase))
            .Where(p => _breaker.CanSend(p.AgentId))
            .Take(MaxParallelPeers)
            .ToList();

        // Также ищем по фразе-приёмнику, если по capability ничего не нашли
        if (peers.Count == 0)
        {
            peers = _registry.FindByPhrase(envelope.Intent)
                .Where(p => !p.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase))
                .Where(p => _breaker.CanSend(p.AgentId))
                .Take(MaxParallelPeers)
                .ToList();
        }

        // 2. Нет peer'ов — обрабатываем локально
        if (peers.Count == 0)
        {
            IntentResponse localResponse = await ProcessLocallyAsync(envelope, ct);
            return new FanOutResult
            {
                Winner = localResponse,
                AllResponses = [localResponse],
                SelectionMethod = "local",
                Duration = sw.Elapsed
            };
        }

        // 3. Один peer — отправляем ему (с retry)
        if (peers.Count < MinPeersForFanOut)
        {
            IntentResponse singleResponse = await SendWithRetryAsync(peers[0].AgentId, envelope, ct);
            return new FanOutResult
            {
                Winner = singleResponse.IsSuccess
                    ? singleResponse
                    : null,
                AllResponses = [singleResponse],
                SelectionMethod = "single-peer",
                Duration = sw.Elapsed
            };
        }

        // 4. Fan-out: отправляем нескольким peer'ам параллельно
        List<IntentResponse> responses = await FanOutAsync(peers, envelope, ct);

        // 5. Fan-in: выбираем лучший ответ
        (IntentResponse? winner, var method, var rationale) = await SelectBestAsync(envelope, responses);

        sw.Stop();
        return new FanOutResult
        {
            Winner = winner,
            AllResponses = responses,
            SelectionMethod = method,
            Duration = sw.Elapsed,
            JudgeRationale = rationale
        };
    }

    /// <summary>
    ///     Отправить intent нескольким peer'ам параллельно и собрать ответы.
    /// </summary>
    private async Task<List<IntentResponse>> FanOutAsync(
        List<RegistryAgentEntry> peers, IntentEnvelope envelope, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(FanOutTimeoutMs);

        IEnumerable<Task<IntentResponse>> tasks = peers.Select(peer => SendWithRetryAsync(peer.AgentId, envelope, cts.Token));
        IntentResponse[] responses = await Task.WhenAll(tasks);
        return responses.ToList();
    }

    /// <summary>
    ///     Отправить запрос peer'у с retry и circuit breaker.
    /// </summary>
    private async Task<IntentResponse> SendWithRetryAsync(
        string agentId, IntentEnvelope envelope, CancellationToken ct)
    {
        for (var attempt = 0; attempt < _retry.MaxAttempts; attempt++)
        {
            if (!_breaker.CanSend(agentId))
            {
                return IntentResponse.Rejected(envelope.RequestId, agentId,
                    "Circuit breaker open — peer temporarily unavailable", envelope.TraceId);
            }

            IntentResponse response = await _transport.SendToAsync(agentId, envelope, ct);

            if (response.IsSuccess)
            {
                _breaker.RecordSuccess(agentId);
                return response;
            }

            _breaker.RecordFailure(agentId);

            if (!_retry.ShouldRetry(response, attempt))
            {
                return response;
            }

            TimeSpan delay = _retry.GetDelay(attempt + 1);
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return response;
            }
        }

        return IntentResponse.Failed(envelope.RequestId, agentId,
            $"Exhausted {_retry.MaxAttempts} attempts", envelope.TraceId);
    }

    /// <summary>
    ///     Выбрать лучший ответ из всех полученных (fan-in).
    /// </summary>
    private async Task<(IntentResponse? Winner, string Method, string? Rationale)> SelectBestAsync(
        IntentEnvelope envelope, List<IntentResponse> responses)
    {
        var successful = responses.Where(r => r.IsSuccess).ToList();
        if (successful.Count == 0)
        {
            return (null, "no-success", null);
        }

        if (successful.Count == 1)
        {
            return (successful[0], "only-success", null);
        }

        switch (Strategy)
        {
            case FanOutStrategy.FirstSuccess:
                return (successful[0], "first-success", null);

            case FanOutStrategy.HighestConfidence:
                IntentResponse best = successful
                    .OrderByDescending(r => r.Confidence ?? 0)
                    .First();
                return (best, "highest-confidence", null);

            case FanOutStrategy.LlmJudge:
                return await JudgeWithLlmAsync(envelope, successful);

            default:
                return (successful[0], "default", null);
        }
    }

    /// <summary>
    ///     LLM-judge: отправить все ответы в LLM и попросить выбрать лучший.
    /// </summary>
    private async Task<(IntentResponse Winner, string Method, string? Rationale)> JudgeWithLlmAsync(
        IntentEnvelope envelope, List<IntentResponse> responses)
    {
        var optionsText = new StringBuilder();
        for (var i = 0; i < responses.Count; i++)
        {
            optionsText.AppendLine($"### Вариант {i + 1} (от {responses[i].Agent}, confidence={responses[i].Confidence})");
            optionsText.AppendLine(responses[i].Result ?? "(пустой ответ)");
            optionsText.AppendLine();
        }

        var prompt = $$"""
                       Ты — судья (LLM-judge) в multi-agent системе. Пользователь задал вопрос,
                       и несколько агентов дали ответы. Выбери лучший ответ.

                       Вопрос: {{envelope.Payload}}

                       {{optionsText}}

                       Верни СТРОГО валидный JSON:
                       {
                         "best_index": <номер лучшего варианта, начиная с 1>,
                         "rationale": "<краткое объяснение выбора на русском>"
                       }
                       """;

        try
        {
            LlmResponse llmResp = await _llm.CompleteAsync([
                new ChatTurn(ChatRole.System, "Ты — LLM-judge. Возвращаешь только JSON."),
                new ChatTurn(ChatRole.User, prompt)
            ]);

            var json = ExtractJson(llmResp.Text);
            using var doc = JsonDocument.Parse(json);
            var bestIndex = doc.RootElement.GetProperty("best_index").GetInt32() - 1;
            var rationale = doc.RootElement.GetProperty("rationale").GetString();

            if (bestIndex >= 0 && bestIndex < responses.Count)
            {
                return (responses[bestIndex], "llm-judge", rationale);
            }
        }
        catch
        {
            // Fallback — возвращаем ответ с высшей уверенностью
        }

        IntentResponse fallback = responses.OrderByDescending(r => r.Confidence ?? 0).First();
        return (fallback, "llm-judge-fallback", "LLM-judge failed, used highest confidence");
    }

    private async Task<IntentResponse> ProcessLocallyAsync(IntentEnvelope envelope, CancellationToken ct)
    {
        var ownAgentId = _manifestService.Current.AgentId;
        try
        {
            AgentResponse response = await _agent.ProcessMessageAsync(envelope.Payload, ct);
            var confidence = response.Confidence switch
            {
                "high" => 0.9,
                "medium" => 0.5,
                _ => 0.2
            };
            return IntentResponse.Ok(envelope.RequestId, ownAgentId, response.Answer,
                response.Mode, response.UsedSkill?.Meta.Name, confidence, envelope.TraceId);
        }
        catch (OperationCanceledException)
        {
            return IntentResponse.TimedOut(envelope.RequestId, ownAgentId, envelope.TraceId);
        }
        catch (Exception ex)
        {
            return IntentResponse.Failed(envelope.RequestId, ownAgentId,
                $"{ex.GetType().Name}: {ex.Message}", envelope.TraceId);
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : "{}";
    }
}
