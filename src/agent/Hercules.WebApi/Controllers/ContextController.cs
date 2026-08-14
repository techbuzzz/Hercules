using Hercules.Context;
using Hercules.Context.Summarizer;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты context assembly: бюджет, summary, trace compression.
///     Task 027 — Context Assembly.
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

        // GET /api/context/summary — summary текущего context
        app.MapGet("/api/context/summary", (IContextBuilder? ctxBuilder) =>
        {
            if (ctxBuilder is null)
            {
                return Results.NotFound(new { error = "ContextBuilder not available (context assembly disabled)" });
            }

            var budget = ctxBuilder.GetCurrentBudget();
            return Results.Ok(new
            {
                assembly = new
                {
                    budget.MaxTokens,
                    budget.UsedTokens,
                    budget.RemainingTokens,
                    budgetUsedPct = budget.MaxTokens > 0
                        ? Math.Round((double)budget.UsedTokens / budget.MaxTokens * 100, 1)
                        : 0
                },
                message = "Context assembled using IContextBuilder (task_027)"
            });
        }).WithName("ContextSummary");

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
}
