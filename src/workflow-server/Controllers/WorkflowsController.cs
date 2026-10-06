using System.Text.Json;
using Hercules.WorkflowServer.Models;
using Hercules.WorkflowServer.Storage;

namespace Hercules.WorkflowServer.Controllers;

/// <summary>
///     REST API для workflow definitions и executions (task_104, ADR-0008).
///     Все маршруты под <c>/api/workflows/*</c> защищены <c>ClientAuthMiddleware</c>
///     (кроме health-check, который выделен отдельно в <see cref="HealthController"/>).
///     Endpoints:
///     - <c>POST   /api/workflows</c> — save definition
///     - <c>GET    /api/workflows</c> — list definitions (summary, без graphJson)
///     - <c>GET    /api/workflows/{id}</c> — get definition (full)
///     - <c>DELETE /api/workflows/{id}</c> — delete definition
///     - <c>POST   /api/workflows/{id}/run</c> — start execution (stub до task_105)
///     - <c>GET    /api/workflows/{id}/executions</c> — list executions (stub)
///     - <c>GET    /api/workflows/executions/{eid}</c> — execution status (stub)
/// </summary>
public static class WorkflowsController
{
    /// <summary>R34: bounds on GET /api/workflows. Unbounded list responses are a cheap DoS.</summary>
    private const int DefaultPageSize = 50;

    private const int MaxPageSize = 200;

    // R34: input length caps. Without these a single POST could persist an arbitrarily
    // large graph into SQLite and blow up the response payload.
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 4_000;
    private const int MaxGraphBytes = 1_000_000;

    public static void MapWorkflows(this IEndpointRouteBuilder app)
    {
        // POST /api/workflows — save (create or update) definition
        app.MapPost("/api/workflows", async (
            SaveWorkflowRequest req,
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.Problem(
                    title: "Invalid workflow",
                    detail: "name is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (req.GraphJson.ValueKind == JsonValueKind.Undefined)
            {
                return Results.Problem(
                    title: "Invalid workflow",
                    detail: "graphJson is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // R34: validate before persisting.
            if (req.Name.Length > MaxNameLength)
            {
                return Results.Problem(
                    title: "Invalid workflow",
                    detail: $"name exceeds {MaxNameLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (req.Description is { Length: > MaxDescriptionLength })
            {
                return Results.Problem(
                    title: "Invalid workflow",
                    detail: $"description exceeds {MaxDescriptionLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var graphBytes = System.Text.Encoding.UTF8.GetByteCount(req.GraphJson.GetRawText());
            if (graphBytes > MaxGraphBytes)
            {
                return Results.Problem(
                    title: "Invalid workflow",
                    detail: $"graphJson exceeds {MaxGraphBytes} bytes.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            // An update must land on an existing definition. Without this guard the upsert
            // (ON CONFLICT DO UPDATE) would INSERT a client-chosen id that does not
            // exist yet, so a typo in the id would quietly create a new workflow
            // instead of reporting that there was nothing to update.
            if (!string.IsNullOrWhiteSpace(req.Id) &&
                await store.GetAsync(req.Id, ct).ConfigureAwait(false) is null)
            {
                return Results.Problem(
                    title: "Not found",
                    detail: $"Workflow '{req.Id}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var def = new WorkflowDefinition
            {
                Id = req.Id ?? "",
                Name = req.Name,
                Version = req.Version <= 0 ? 1 : req.Version,
                Description = req.Description,
                GraphJson = req.GraphJson,
            };
            var saved = await store.SaveAsync(def, ct).ConfigureAwait(false);
            return Results.Created($"/api/workflows/{saved.Id}", saved);
        }).WithName("SaveWorkflow");

        // GET /api/workflows — list definitions (paginated, summary only)
        app.MapGet("/api/workflows", async (
            int? limit,
            string? cursor,
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            // R34: the endpoint previously returned every definition with no bound, so a
            // large store turned a single GET into an unbounded response.
            var take = limit ?? DefaultPageSize;
            if (take <= 0)
            {
                return Results.Problem(
                    title: "Invalid pagination",
                    detail: "limit must be greater than 0.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (take > MaxPageSize)
            {
                take = MaxPageSize;
            }

            var all = await store.ListAsync(ct).ConfigureAwait(false);

            IReadOnlyList<WorkflowSummary> page;
            if (string.IsNullOrEmpty(cursor))
            {
                page = all.Take(take).ToList();
            }
            else
            {
                // Opaque cursor = index into the stable ordering the store returns.
                if (!int.TryParse(cursor, out var start) || start < 0 || start > all.Count)
                {
                    return Results.Problem(
                        title: "Invalid pagination",
                        detail: "cursor is not a valid position for this collection.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                page = all.Skip(start).Take(take).ToList();
            }

            var nextCursor = (int.TryParse(cursor, out var from) ? from + page.Count : page.Count);
            var hasMore = nextCursor < all.Count;

            return Results.Ok(new
            {
                items = page,
                count = page.Count,
                total = all.Count,
                nextCursor = hasMore ? nextCursor.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            });
        }).WithName("ListWorkflows");

        // GET /api/workflows/{id} — get full definition
        app.MapGet("/api/workflows/{id}", async (
            string id,
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            var def = await store.GetAsync(id, ct).ConfigureAwait(false);
            return def is null
                ? Results.NotFound(new { error = $"Workflow '{id}' not found." })
                : Results.Ok(def);
        }).WithName("GetWorkflow");

        // DELETE /api/workflows/{id} — delete definition
        app.MapDelete("/api/workflows/{id}", async (
            string id,
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct).ConfigureAwait(false);
            return deleted
                ? Results.NoContent()
                : Results.NotFound(new { error = $"Workflow '{id}' not found." });
        }).WithName("DeleteWorkflow");

        // POST /api/workflows/{id}/run — start execution (stub до task_105)
        app.MapPost("/api/workflows/{id}/run", async (
            string id,
            IWorkflowDefinitionStore store,
            RunWorkflowRequest? body,
            CancellationToken ct) =>
        {
            var def = await store.GetAsync(id, ct).ConfigureAwait(false);
            if (def is null)
            {
                return Results.Problem(
                    title: "Not found",
                    detail: $"Workflow '{id}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // R34: RunWorkflowRequest was accepted and then silently discarded — the API
            // documented a run payload it never read. Reject a supplied body explicitly
            // rather than pretending to honour it.
            if (body?.Input is { } input && input.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                return Results.Problem(
                    title: "Not implemented",
                    detail: "Workflow execution is not implemented yet (task_105); run inputs cannot be accepted.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Problem(
                title: "Not implemented",
                detail: "Workflow execution is not implemented yet (task_104 follow-up, see task_105).",
                statusCode: StatusCodes.Status501NotImplemented,
                extensions: new Dictionary<string, object?>
                {
                    ["workflowId"] = id,
                    ["workflowName"] = def.Name,
                });
        }).WithName("RunWorkflow");

        // GET /api/workflows/{id}/executions — list executions for a workflow (stub)
        app.MapGet("/api/workflows/{id}/executions", async (
            string id,
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            var def = await store.GetAsync(id, ct).ConfigureAwait(false);
            if (def is null)
            {
                return Results.Problem(
                    title: "Not found",
                    detail: $"Workflow '{id}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // R35: RFC 7807 instead of a 501 carrying a mixed error/data body.
            return Results.Problem(
                title: "Not implemented",
                detail: "Execution listing is not implemented yet (task_105).",
                statusCode: StatusCodes.Status501NotImplemented,
                extensions: new Dictionary<string, object?> { ["workflowId"] = id });
        }).WithName("ListWorkflowExecutions");

        // GET /api/workflows/executions/{eid} — single execution status (stub)
        app.MapGet("/api/workflows/executions/{eid}", (string eid) =>
            Results.Problem(
                title: "Not implemented",
                detail: "Execution status is not implemented yet (task_105).",
                statusCode: StatusCodes.Status501NotImplemented,
                extensions: new Dictionary<string, object?> { ["executionId"] = eid }))
            .WithName("GetWorkflowExecution");
    }
}
