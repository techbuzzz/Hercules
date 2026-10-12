using Hercules.Tasks;
using Microsoft.AspNetCore.Routing;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI endpoints for durable task lifecycle (task_018, task_108).
/// </summary>
public static class TaskProgressController
{
    public static void MapTasks(this IEndpointRouteBuilder app)
    {
        // POST /api/tasks — create a durable task
        app.MapPost("/api/tasks", async (
            CreateTaskRequest request,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            var id = await svc.StartAsync(request, ct);
            return Results.Accepted($"/api/tasks/{id}", new { taskId = id.ToString(), status = "Running" });
        }).WithName("CreateTask").WithTags("Tasks");

        // GET /api/tasks — list tasks with optional status filter
        app.MapGet("/api/tasks", async (
            ITaskRepository repo,
            string? status,
            int limit = 100,
            CancellationToken ct = default) =>
        {
            DurableTaskStatus? filter = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Tasks.DurableTaskStatus>(status, true, out var parsed))
            {
                filter = parsed;
            }

            var tasks = await repo.ListAsync(filter, limit, ct);
            return Results.Ok(new
            {
                count = tasks.Count,
                tasks = tasks.Select(ToDto).ToList()
            });
        }).WithName("ListTasks").WithTags("Tasks");

        // GET /api/tasks/{id} — get task status
        app.MapGet("/api/tasks/{id}", async (
            string id,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            var task = await svc.GetStatusAsync(new TaskId(Guid.Parse(id)), ct);
            return task is null
                ? Results.NotFound(new { error = $"Task '{id}' not found." })
                : Results.Ok(ToDto(task));
        }).WithName("GetTask").WithTags("Tasks");

        // POST /api/tasks/{id}/pause — pause a running task
        app.MapPost("/api/tasks/{id}/pause", async (
            string id,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            await svc.PauseAsync(new TaskId(Guid.Parse(id)), ct);
            return Results.Ok(new { message = $"Task '{id}' paused." });
        }).WithName("PauseTask").WithTags("Tasks");

        // POST /api/tasks/{id}/resume — resume a paused task
        app.MapPost("/api/tasks/{id}/resume", async (
            string id,
            string? checkpointId,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            await svc.ResumeAsync(new TaskId(Guid.Parse(id)), checkpointId, ct);
            return Results.Ok(new { message = $"Task '{id}' resumed.", checkpointId });
        }).WithName("ResumeTask").WithTags("Tasks");

        // POST /api/tasks/{id}/cancel — cancel a task
        app.MapPost("/api/tasks/{id}/cancel", async (
            string id,
            string? reason,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            await svc.CancelAsync(new TaskId(Guid.Parse(id)), reason, ct);
            return Results.Ok(new { message = $"Task '{id}' cancelled.", reason });
        }).WithName("CancelTask").WithTags("Tasks");

        // POST /api/tasks/{id}/checkpoints — create a checkpoint
        app.MapPost("/api/tasks/{id}/checkpoints", async (
            string id,
            CreateCheckpointRequest request,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            var taskId = new TaskId(Guid.Parse(id));
            var task = await svc.GetStatusAsync(taskId, ct);
            if (task is null)
            {
                return Results.NotFound(new { error = $"Task '{id}' not found." });
            }
            var snapshot = request.StateSnapshot ?? "{}";
            var checkpointId = await svc.CreateCheckpointAsync(
                taskId, request.StepNumber, snapshot, ct);
            await svc.IncrementStepAsync(taskId, ct);
            return Results.Created(
                $"/api/tasks/{id}/checkpoints/{checkpointId}",
                new
                {
                    id = checkpointId,
                    taskId = id,
                    stepNumber = request.StepNumber,
                    snapshotBytes = snapshot.Length,
                    createdAt = DateTime.UtcNow
                });
        }).WithName("CreateTaskCheckpoint").WithTags("Tasks");

        // GET /api/tasks/{id}/checkpoints — list task checkpoints
        app.MapGet("/api/tasks/{id}/checkpoints", async (
            string id,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            var checkpoints = await svc.ListCheckpointsAsync(new TaskId(Guid.Parse(id)), ct);
            return Results.Ok(new
            {
                taskId = id,
                count = checkpoints.Count,
                checkpoints = checkpoints.Select(c => new
                {
                    id = c.Id,
                    taskId = c.TaskId.ToString(),
                    stepNumber = c.StepNumber,
                    createdAt = c.CreatedAt
                }).ToList()
            });
        }).WithName("ListTaskCheckpoints").WithTags("Tasks");

        // GET /api/tasks/{id}/checkpoints/{ckptId} — load a specific checkpoint
        app.MapGet("/api/tasks/{id}/checkpoints/{ckptId}", async (
            string id,
            string ckptId,
            ITaskExecutionService svc,
            CancellationToken ct) =>
        {
            var checkpoint = await svc.LoadCheckpointAsync(ckptId, ct);
            if (checkpoint is null || checkpoint.TaskId.ToString() != id)
            {
                return Results.NotFound(new { error = $"Checkpoint '{ckptId}' not found for task '{id}'." });
            }
            return Results.Ok(new
            {
                id = checkpoint.Id,
                taskId = checkpoint.TaskId.ToString(),
                stepNumber = checkpoint.StepNumber,
                stateSnapshot = checkpoint.StateSnapshot,
                createdAt = checkpoint.CreatedAt
            });
        }).WithName("GetTaskCheckpoint").WithTags("Tasks");
    }

    private static object ToDto(Tasks.DurableTask task)
    {
        return new
        {
            id = task.Id.ToString(),
            name = task.Name,
            status = task.Status.ToString(),
            skillId = task.Metadata.SkillId,
            sessionId = task.Metadata.SessionId,
            priority = task.Metadata.Priority,
            tags = task.Metadata.Tags,
            createdBy = task.Metadata.CreatedBy,
            description = task.Metadata.Description,
            attemptCount = task.AttemptCount,
            currentStep = task.CurrentStep,
            result = task.Result,
            error = task.Error,
            createdAt = task.CreatedAt,
            updatedAt = task.UpdatedAt,
            completedAt = task.CompletedAt,
            cancellationReason = task.CancellationReason
        };
    }
}

/// <summary>
///     Request body for <c>POST /api/tasks/{id}/checkpoints</c>.
/// </summary>
public sealed record CreateCheckpointRequest(int StepNumber, string? StateSnapshot);
