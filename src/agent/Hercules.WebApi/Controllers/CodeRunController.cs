using System.Text;
using System.Text.Json;
using Hercules.CodeExecution;
using Hercules.WebApi.CodeRuns;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// Sandbox code execution with an SSE result stream.
/// <para>
/// Replaces the local terminal the Electron shell used to show (ADR-0009). The
/// code runs inside the agent's sandbox via <see cref="ICodeExecutor"/> — there is
/// no client-side process to spawn and no PTY to emulate.
/// </para>
/// <para>
/// Note on naming: the migration plan called this <c>/api/skills/{id}/run</c>.
/// <c>Skill</c> (<c>Storage/Models.cs</c>) has no code field — a skill is
/// <c>Meta + Description + Prompt</c> and is executed by the LLM, not by the
/// sandbox. A run therefore targets a piece of code (a file-based C# skill under
/// test, or a snippet from the editor), not a stored skill.
/// </para>
/// </summary>
public static class CodeRunController
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapCodeRuns(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/code").WithTags("CodeRun");

        // POST /api/code/run — start a sandbox run. Returns immediately with a runId;
        // observe it over SSE so the UI never blocks on a long execution.
        group.MapPost("/run", async (
            RunRequest? req,
            SkillRunStore store,
            IEnumerable<ICodeExecutor> executors,
            CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Code))
                return Results.BadRequest(new { error = "code is required" });

            var language = string.IsNullOrWhiteSpace(req.Language) ? "csharp" : req.Language.Trim().ToLowerInvariant();

            var executor = executors.FirstOrDefault(e => e.SupportedLanguages.Contains(language));
            if (executor is null)
                return Results.BadRequest(new
                {
                    error = $"no executor supports language '{language}'",
                    supported = executors.SelectMany(e => e.SupportedLanguages).Distinct(),
                });

            var run = store.Create(req.SkillId, language);
            run.Append("start", new { runId = run.Id, executor = executor.Name, language });

            // Fire-and-forget: the run's lifetime is owned by the store, not the request.
            _ = Task.Run(async () =>
            {
                try
                {
                    run.MarkRunning();
                    var result = await executor.ExecuteAsync(
                        new ExecutionRequest(
                            req.Code,
                            language,
                            req.Args,
                            req.TimeoutMs,
                            req.MemoryLimitMb,
                            req.WorkingDir),
                        CancellationToken.None);

                    if (result.Status == "rejected")
                        run.Append("rejected", new { result.BlockedPatterns });

                    run.Append("result", new
                    {
                        result.ExitCode,
                        result.Stdout,
                        result.Stderr,
                        result.DurationMs,
                        result.Status,
                        result.SessionDir,
                        success = result.IsSuccess,
                    });
                    run.Complete(result);
                }
                catch (Exception ex)
                {
                    run.Append("exception", new { ex.GetType().Name, ex.Message });
                    run.Fail(ex.Message);
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/code/run/{run.Id}/stream", new { runId = run.Id });
        })
        .WithName("StartCodeRun")
        .RequireRateLimiting(RateLimitPolicies.Expensive);

        // GET /api/code/run/{runId} — status snapshot (polling alternative to SSE).
        group.MapGet("/run/{runId}", (string runId, SkillRunStore store) =>
        {
            var run = store.Get(runId);
            return run is null
                ? Results.NotFound(new { error = "unknown run" })
                : Results.Ok(run.StatusSnapshot());
        })
        .WithName("GetCodeRun");

        // GET /api/code/run/{runId}/stream — SSE.
        // `?after=<seq>` replays from the client's last seen sequence, so a dropped
        // connection resumes instead of restarting the run.
        group.MapGet("/run/{runId}/stream", async (
            string runId,
            long? after,
            SkillRunStore store,
            HttpContext http) =>
        {
            var run = store.Get(runId);
            if (run is null) return Results.NotFound(new { error = "unknown run" });

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            // Disable proxy buffering (nginx and friends) so events are not held back.
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var cursor = after ?? 0;
            var poll = TimeSpan.FromMilliseconds(200);

            while (!http.RequestAborted.IsCancellationRequested)
            {
                var (events, truncated, terminal) = run.Snapshot(cursor);

                if (truncated)
                {
                    await WriteAsync(http, "truncated", new
                    {
                        message = "older events were dropped from the replay buffer",
                        resumeFrom = cursor,
                    }, CancellationToken.None);
                }

                foreach (var evt in events)
                {
                    cursor = evt.Seq;
                    await WriteAsync(http, evt.Type, evt.Data, http.RequestAborted);
                }

                if (terminal)
                {
                    await WriteAsync(http, "done", run.StatusSnapshot(), http.RequestAborted);
                    break;
                }

                await Task.Delay(poll, http.RequestAborted);
            }

            return Results.Empty;
        })
        .WithName("StreamCodeRun");
    }

    private static async Task WriteAsync(HttpContext http, string type, object? data, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(data ?? new { }, Json);
        var sb = new StringBuilder();
        sb.Append("event: ").Append(type).Append('\n');
        // Data must be single-line: JSON escaping keeps newlines out of it.
        sb.Append("data: ").Append(payload).Append("\n\n");

        await http.Response.WriteAsync(sb.ToString(), ct);
        await http.Response.Body.FlushAsync(ct);
    }
}

/// <summary>Body for <c>POST /api/code/run</c>.</summary>
public sealed class RunRequest
{
    /// <summary>Source to execute (a file-based C# skill under test, or an editor snippet).</summary>
    public string? Code { get; set; }

    public string? Language { get; set; }
    public string[]? Args { get; set; }
    public int? TimeoutMs { get; set; }
    public int? MemoryLimitMb { get; set; }
    public string? WorkingDir { get; set; }

    /// <summary>Optional skill this run relates to, for correlation in the UI.</summary>
    public string? SkillId { get; set; }
}