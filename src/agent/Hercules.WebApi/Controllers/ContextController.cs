using Hercules.Config;
using Hercules.Context;
using Hercules.Context.Distillation;
using Hercules.Context.Summarizer;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты context assembly: бюджет, summary, trace compression, дистилляция.
///     Task 027 — Context Assembly.
///     Task 102 — Context Distillation (иерархическая компрессия recent/older/ancient).
/// </summary>
public static class ContextController
{
    public static void MapContext(this IEndpointRouteBuilder app)
    {
        // GET /api/context/budget — текущий context budget
        app.MapGet("/api/context/budget", (IContextBuilder? ctxBuilder) =>
        {
            if (ctxBuilder is null)
            {
                return Results.NotFound(new { error = "ContextBuilder not available (context assembly disabled)" });
            }

            var budget = ctxBuilder.GetCurrentBudget();
            return Results.Ok(new
            {
                budget.MaxTokens,
                budget.UsedTokens,
                budget.RemainingTokens,
                budgetUsedPct = budget.MaxTokens > 0
                    ? Math.Round((double)budget.UsedTokens / budget.MaxTokens * 100, 1)
                    : 0
            });
        }).WithName("ContextBudget");

        // GET /api/context/summary?sessionId=... — markdown-сводка дистиллированного контекста.
        // task_102: расширено — теперь возвращает реальный summary вместо заглушки.
        app.MapGet("/api/context/summary", async (
            string sessionId,
            IContextBuilder? ctxBuilder,
            ContextDistillationService? distillation,
            ContextConfig cfg,
            int? maxTokens,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new { error = "sessionId is required" });
            }

            if (distillation is null)
            {
                return Results.NotFound(new { error = "Distillation service not available" });
            }

            int tokens = maxTokens ?? (cfg.Distillation.SummaryTokenBudget + cfg.Distillation.KeyFactsTokenBudget);
            var markdown = await distillation.GetSummaryAsync(sessionId, cfg.Distillation, tokens, ct);
            return Results.Ok(new
            {
                sessionId,
                mode = cfg.Distillation.Mode.ToString(),
                summary = markdown,
                empty = string.IsNullOrWhiteSpace(markdown)
            });
        }).WithName("ContextSummary");

        // POST /api/context/distill — запуск дистилляции для сессии (task_102).
        // Body: { "sessionId": "...", "mode": "off|auto|manual" } (mode optional, defaults to current config).
        app.MapPost("/api/context/distill", async (
            DistillRequest req,
            ContextDistillationService? distillation,
            ContextConfig cfg,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (distillation is null)
            {
                return Results.NotFound(new { error = "Distillation service not available" });
            }
            if (req is null || string.IsNullOrWhiteSpace(req.SessionId))
            {
                return Results.BadRequest(new { error = "sessionId is required" });
            }

            // Override mode for this call if requested.
            var effectiveCfg = cfg.Distillation;
            if (!string.IsNullOrWhiteSpace(req.Mode)
                && Enum.TryParse<DistillationMode>(req.Mode, ignoreCase: true, out var parsed))
            {
                effectiveCfg = new DistillationConfig
                {
                    Mode = parsed,
                    Strategy = cfg.Distillation.Strategy,
                    RecentRawCount = cfg.Distillation.RecentRawCount,
                    SummaryInterval = cfg.Distillation.SummaryInterval,
                    KeyFactsExtraction = cfg.Distillation.KeyFactsExtraction,
                    MaxAncientFacts = cfg.Distillation.MaxAncientFacts,
                    SummaryTokenBudget = cfg.Distillation.SummaryTokenBudget,
                    KeyFactsTokenBudget = cfg.Distillation.KeyFactsTokenBudget,
                    Preset = cfg.Distillation.Preset
                };
            }

            if (effectiveCfg.Mode == DistillationMode.Off)
            {
                return Results.BadRequest(new { error = "Distillation mode is Off — enable it in config or pass mode=auto|manual" });
            }

            try
            {
                var result = await distillation.DistillAsync(req.SessionId, effectiveCfg, ct);
                return Results.Ok(new
                {
                    result.SessionId,
                    result.RecentCount,
                    result.SummariesCreated,
                    result.KeyFactsExtracted,
                    result.TokensBefore,
                    result.TokensAfter,
                    tokenSavingsPct = result.TokensBefore > 0
                        ? Math.Round((1.0 - (double)result.TokensAfter / result.TokensBefore) * 100, 1)
                        : 0,
                    result.SummaryMarkdown
                });
            }
            catch (Exception ex)
            {
                var log = loggerFactory.CreateLogger("ContextController.Distill");
                log.LogError(ex, "[ContextController] Distill failed for session {SessionId}", req.SessionId);
                return Results.Problem(detail: ex.Message, statusCode: 500, title: "Distill failed");
            }
        }).WithName("ContextDistill");

        // POST /api/context/trace/compress — сжать tool trace в episodic memory
        app.MapPost("/api/context/trace/compress", async (
            IContextBuilder? ctxBuilder,
            List<ToolTraceEntry> trace,
            string sessionId,
            CancellationToken ct) =>
        {
            if (ctxBuilder is null)
            {
                return Results.NotFound(new { error = "ContextBuilder not available" });
            }

            if (trace.Count == 0)
            {
                return Results.BadRequest(new { error = "Trace must contain at least one entry" });
            }

            var compressed = await ctxBuilder.CompressTraceAsync(trace, sessionId, ct);
            return Results.Ok(new
            {
                compressed,
                traceCount = trace.Count,
                sessionId
            });
        }).WithName("ContextTraceCompress");
    }

    /// <summary>Request body для <c>POST /api/context/distill</c> (task_102).</summary>
    public sealed record DistillRequest(string SessionId, string? Mode);
}
