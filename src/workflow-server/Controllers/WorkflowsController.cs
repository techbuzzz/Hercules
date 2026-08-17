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
                return Results.BadRequest(new { error = "name is required." });
            }
            if (req.GraphJson.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "graphJson is required." });
            }
            var def = new WorkflowDefinition
            {
                Name = req.Name,
                Version = req.Version <= 0 ? 1 : req.Version,
                Description = req.Description,
                GraphJson = req.GraphJson,
            };
            var saved = await store.SaveAsync(def, ct).ConfigureAwait(false);
            return Results.Created($"/api/workflows/{saved.Id}", saved);
        }).WithName("SaveWorkflow");

        // GET /api/workflows — list definitions
        app.MapGet("/api/workflows", async (
            IWorkflowDefinitionStore store,
            CancellationToken ct) =>
        {
            var list = await store.ListAsync(ct).ConfigureAwait(false);
            return Results.Ok(list);
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
                return Results.NotFound(new { error = $"Workflow '{id}' not found." });
            }
            // task_105: real executor + persistence + retries.
            // На этом этапе возвращаем 501 с явным follow-up указателем.
            return Results.Json(
                new
                {
                    error = "Workflow execution not implemented yet (task_104 follow-up, see task_105).",
                    workflowId = id,
                    workflowName = def.Name,
                },
                statusCode: StatusCodes.Status501NotImplemented);
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
                return Results.NotFound(new { error = $"Workflow '{id}' not found." });
            }
            return Results.Json(
                new
                {
                    error = "Execution listing not implemented yet (task_104 follow-up, see task_105).",
                    workflowId = id,
                    executions = Array.Empty<object>(),
                },
                statusCode: StatusCodes.Status501NotImplemented);
        }).WithName("ListWorkflowExecutions");

        // GET /api/workflows/executions/{eid} — single execution status (stub)
        app.MapGet("/api/workflows/executions/{eid}", (
            string eid) =>
        {
            return Results.Json(
                new
                {
                    error = "Execution status not implemented yet (task_104 follow-up, see task_105).",
                    executionId = eid,
                },
                statusCode: StatusCodes.Status501NotImplemented);
        }).WithName("GetWorkflowExecution");
    }
}
