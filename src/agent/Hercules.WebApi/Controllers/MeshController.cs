using Hercules.Mesh;
using Hercules.Mesh.TaskLifecycle;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Mesh-эндпоинты: манифест агента, capability registry, inter-agent intent routing,
///     fan-out оркестрация (Phase 4), distributed reflection, shared memory sync, circuit breakers.
/// </summary>
public static class MeshController
{
    public static void MapMesh(this IEndpointRouteBuilder app)
    {
        // === Phase 3: Manifest + Registry + Intent ===

        // GET /agent.manifest.json — публичный манифест (well-known URL для discovery).
        app.MapGet("/agent.manifest.json", (AgentManifestService manifestService) =>
        {
            var manifest = manifestService.Current;
            return Results.Ok(manifest);
        }).WithName("GetAgentManifest");

        // GET /api/mesh/agents — список известных агентов в реестре (полная запись с health/trust/cost)
        app.MapGet("/api/mesh/agents", (ICapabilityRegistryService registry) =>
        {
            var agents = registry.ListAll();
            return Results.Ok(new { count = agents.Count, agents });
        }).WithName("ListMeshAgents");

        // GET /api/mesh/agents/{id} — полная запись агента по ID (health, trust, cost/latency, TTL)
        app.MapGet("/api/mesh/agents/{id}", (string id, ICapabilityRegistryService registry) =>
        {
            var entry = registry.GetEntry(id);
            return entry is null
                ? Results.NotFound(new { error = $"Агент '{id}' не найден в реестре." })
                : Results.Ok(entry);
        }).WithName("GetMeshAgentFull");

        // GET /api/mesh/agents/{id}/health — health status агента
        app.MapGet("/api/mesh/agents/{id}/health", (string id, ICapabilityRegistryService registry) =>
        {
            var entry = registry.GetEntry(id);
            if (entry is null)
                return Results.NotFound(new { error = $"Агент '{id}' не найден." });
            return Results.Ok(new
            {
                agentId = entry.AgentId,
                healthStatus = entry.HealthStatus,
                lastHealthCheck = entry.LastHealthCheck,
                consecutiveFailures = entry.ConsecutiveFailures,
                trustLevel = entry.TrustLevel,
                costHintUsd = entry.CostHintUsd,
                latencyHintMs = entry.LatencyHintMs,
                expirySeconds = entry.ExpirySeconds
            });
        }).WithName("GetMeshAgentHealth");

        // POST /api/mesh/agents/{id}/touch — heartbeat (обновить last_seen)
        app.MapPost("/api/mesh/agents/{id}/touch", (string id, ICapabilityRegistryService registry) =>
        {
            var entry = registry.GetEntry(id);
            if (entry is null)
                return Results.NotFound(new { error = $"Агент '{id}' не найден." });

            registry.Touch(id);
            return Results.Ok(new { status = "touched", agentId = id });
        }).WithName("TouchMeshAgent");

        // POST /api/mesh/agents/cleanup — cleanup просроченных агентов
        app.MapPost("/api/mesh/agents/cleanup", (ICapabilityRegistryService registry) =>
        {
            int removed = registry.CleanupExpired();
            return Results.Ok(new { status = "cleaned", removedCount = removed });
        }).WithName("CleanupExpiredAgents");

        // POST /api/mesh/agents/register — зарегистрировать peer-агента
        app.MapPost("/api/mesh/agents/register", (AgentManifest manifest, CapabilityRegistry registry) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(manifest.AgentId))
                {
                    return Results.BadRequest(new { error = "agentId обязателен." });
                }

                registry.Register(manifest);
                return Results.Ok(new { status = "registered", agentId = manifest.AgentId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("RegisterMeshAgent");

        // DELETE /api/mesh/agents/{id} — удалить агента из реестра
        app.MapDelete("/api/mesh/agents/{id}", (string id, CapabilityRegistry registry) =>
        {
            var removed = registry.Remove(id);
            return removed
                ? Results.Ok(new { status = "removed", agentId = id })
                : Results.NotFound(new { error = $"Агент '{id}' не найден." });
        }).WithName("RemoveMeshAgent");

        // GET /api/mesh/capabilities — список capabilities
        app.MapGet("/api/mesh/capabilities", (string? agentId, CapabilityRegistry registry) =>
        {
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                return Results.Ok(registry.ListCapabilities(agentId));
            }

            return Results.Ok(registry.ListAgents());
        }).WithName("ListMeshCapabilities");

        // GET /api/mesh/capabilities/{name} — найти агентов по имени capability
        app.MapGet("/api/mesh/capabilities/{name}", (string name, CapabilityRegistry registry) =>
            Results.Ok(registry.FindByCapability(name))).WithName("FindMeshByCapability");

        // GET /api/mesh/capabilities/search?phrase=... — semantic lookup
        app.MapGet("/api/mesh/capabilities/search", (string phrase, CapabilityRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                return Results.BadRequest(new { error = "Параметр 'phrase' обязателен." });
            }

            return Results.Ok(registry.FindByPhrase(phrase));
        }).WithName("SearchMeshByPhrase");

        // POST /api/mesh/intent — отправить intent на маршрутизацию (IntentRouter, single-peer)
        app.MapPost("/api/mesh/intent", async (IntentEnvelope envelope, IntentRouter router, CancellationToken ct) =>
        {
            try
            {
                var response = await router.RouteAsync(envelope, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500, title: "Intent routing error");
            }
        }).WithName("SendMeshIntent");

        // === Phase 4: Fan-out + Circuit Breaker + Reflection + Shared Memory ===

        // POST /api/mesh/fanout — fan-out запрос нескольким peer'ам + выбор лучшего (MeshRouter)
        app.MapPost("/api/mesh/fanout", async (IntentEnvelope envelope, MeshRouter router, CancellationToken ct) =>
        {
            try
            {
                var result = await router.RouteWithFanOutAsync(envelope, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500, title: "Fan-out error");
            }
        }).WithName("MeshFanOut");

        // GET /api/mesh/circuits — состояние circuit breakers всех peer'ов
        app.MapGet("/api/mesh/circuits", (CircuitBreaker breaker) =>
            Results.Ok(breaker.GetAllStates())).WithName("GetCircuitBreakers");

        // POST /api/mesh/circuits/{id}/reset — сбросить circuit breaker для peer'а
        app.MapPost("/api/mesh/circuits/{id}/reset", (string id, CircuitBreaker breaker) =>
        {
            breaker.Reset(id);
            return Results.Ok(new { status = "reset", agentId = id });
        }).WithName("ResetCircuitBreaker");

        // GET /api/mesh/reflect — distributed reflection (производительность mesh)
        app.MapGet("/api/mesh/reflect", async (DistributedReflection reflection, CancellationToken ct) =>
        {
            var result = await reflection.ReflectAsync(ct);
            return Results.Ok(result);
        }).WithName("DistributedReflection");

        // GET /api/mesh/reflect/recommendations — рекомендации по новым локальным навыкам
        app.MapGet("/api/mesh/reflect/recommendations", (DistributedReflection reflection) =>
            Results.Ok(reflection.GetLocalSkillRecommendations())).WithName("ReflectionRecommendations");

        // === Phase 4: Shared Memory Sync ===

        // GET /api/mesh/shared-memory — список локальных shared-фактов
        app.MapGet("/api/mesh/shared-memory", (SharedMemorySync sync) =>
            Results.Ok(sync.GetLocalFacts())).WithName("ListSharedMemory");

        // POST /api/mesh/shared-memory — опубликовать факт для синхронизации
        app.MapPost("/api/mesh/shared-memory", async (SharedMemoryPublishRequest req,
            SharedMemorySync sync, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Category) || string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { error = "category и content обязательны." });
            }

            var fact = await sync.PublishFactAsync(req.Category, req.Content, req.AllowedAgents, ct);
            return Results.Created($"/api/mesh/shared-memory/{fact.Id}", fact);
        }).WithName("PublishSharedMemory");

        // GET /api/mesh/shared-memory/for/{agentId} — факты, доступные указанному агенту
        app.MapGet("/api/mesh/shared-memory/for/{agentId}", (string agentId, SharedMemorySync sync) =>
            Results.Ok(sync.GetFactsForAgent(agentId))).WithName("GetSharedMemoryForAgent");

        // POST /api/mesh/shared-memory/sync — синхронизировать факты с peer'ами
        app.MapPost("/api/mesh/shared-memory/sync", async (SharedMemorySync sync, CancellationToken ct) =>
        {
            var received = await sync.SyncFromPeersAsync(ct);
            return Results.Ok(new { status = "synced", receivedCount = received });
        }).WithName("SyncSharedMemory");

        // DELETE /api/mesh/shared-memory/{factId} — удалить shared-факт
        app.MapDelete("/api/mesh/shared-memory/{factId}", (string factId, SharedMemorySync sync) =>
        {
            var removed = sync.RemoveFact(factId);
            return removed
                ? Results.Ok(new { status = "removed", factId })
                : Results.NotFound(new { error = $"Факт '{factId}' не найден." });
        }).WithName("RemoveSharedMemory");

        // === Phase 3: Task Lifecycle Protocol (task_036) ===

        // POST /api/mesh/tasks/accept — принять delegated задачу
        app.MapPost("/api/mesh/tasks/accept", async (
            DelegatedTaskAcceptRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.AcceptAsync(
                    req.ParentRequestId,
                    req.CallerAgentId,
                    req.Intent,
                    req.Payload,
                    req.Auth,
                    req.ExpiresAt,
                    ct);
                return Results.Accepted($"/api/mesh/tasks/{task.TaskId}", ToTaskDto(task));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("AcceptDelegatedTask");

        // GET /api/mesh/tasks/{id} — получить состояние delegated задачи
        app.MapGet("/api/mesh/tasks/{id}", async (
            string id,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            var task = await protocol.GetStateAsync(id, ct);
            return task is null
                ? Results.NotFound(new { error = $"Delegated task '{id}' not found." })
                : Results.Ok(ToTaskDto(task));
        }).WithName("GetDelegatedTask");

        // POST /api/mesh/tasks/{id}/state — обновить состояние
        app.MapPost("/api/mesh/tasks/{id}/state", async (
            string id,
            DelegatedTaskStateUpdateRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                if (!Enum.TryParse<DelegatedTaskState>(req.State, true, out var newState))
                    return Results.BadRequest(new { error = $"Unknown state: {req.State}" });

                var task = await protocol.UpdateStateAsync(id, newState, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        }).WithName("UpdateDelegatedTaskState");

        // POST /api/mesh/tasks/{id}/await-input — перевести в AwaitingInput
        app.MapPost("/api/mesh/tasks/{id}/await-input", async (
            string id,
            AwaitInputRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.AwaitInputAsync(id, req.InputType, req.Question, req.Choices, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("AwaitDelegatedTaskInput");

        // POST /api/mesh/tasks/{id}/complete — завершить с результатом
        app.MapPost("/api/mesh/tasks/{id}/complete", async (
            string id,
            DelegatedTaskCompleteRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.CompleteAsync(id, req.Result, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CompleteDelegatedTask");

        // POST /api/mesh/tasks/{id}/fail — завершить с ошибкой
        app.MapPost("/api/mesh/tasks/{id}/fail", async (
            string id,
            DelegatedTaskFailRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.FailAsync(id, req.Error, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("FailDelegatedTask");

        // POST /api/mesh/tasks/{id}/cancel — отменить
        app.MapPost("/api/mesh/tasks/{id}/cancel", async (
            string id,
            DelegatedTaskCancelRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.CancelAsync(id, req.Reason, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("CancelDelegatedTask");

        // POST /api/mesh/tasks/{id}/callback — callback от вызывающего агента (input delivered)
        app.MapPost("/api/mesh/tasks/{id}/callback", async (
            string id,
            DelegatedTaskCallbackRequest req,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            try
            {
                var task = await protocol.RecordInputAsync(id, req.Input, ct);
                return Results.Ok(ToTaskDto(task));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("DelegatedTaskCallback");

        // GET /api/mesh/tasks/{id}/poll — long-poll на AwaitingInput
        app.MapGet("/api/mesh/tasks/{id}/poll", async (
            string id,
            int timeoutMs,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            var task = await protocol.PollForInputDeliveryAsync(id, timeoutMs, ct);
            return task is null
                ? Results.NotFound(new { error = $"Delegated task '{id}' not found." })
                : Results.Ok(ToTaskDto(task));
        }).WithName("PollDelegatedTask");

        // POST /api/mesh/tasks/{id}/expire-check — принудительная проверка expiration
        app.MapPost("/api/mesh/tasks/{id}/expire-check", async (
            string id,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            var expired = await protocol.CheckExpiredAsync(id, ct);
            var task = await protocol.GetStateAsync(id, ct);
            return Results.Ok(new { taskId = id, expired, task = task is null ? null : ToTaskDto(task) });
        }).WithName("CheckDelegatedTaskExpired");

        // POST /api/mesh/tasks/{id}/notify — отправить callback вызывающему агенту
        app.MapPost("/api/mesh/tasks/{id}/notify", async (
            string id,
            ITaskLifecycleProtocol protocol,
            CancellationToken ct) =>
        {
            await protocol.NotifyStateChangeAsync(id, ct);
            return Results.Ok(new { taskId = id, notified = true });
        }).WithName("NotifyDelegatedTaskState");
    }

    private static object ToTaskDto(DelegatedTask task)
    {
        return new
        {
            taskId = task.TaskId,
            parentRequestId = task.ParentRequestId,
            callerAgentId = task.CallerAgentId,
            intent = task.Intent,
            state = task.State.ToString(),
            createdAt = task.CreatedAt,
            updatedAt = task.UpdatedAt,
            completedAt = task.CompletedAt,
            result = task.Result,
            error = task.Error,
            cancellationReason = task.CancellationReason,
            expiresAt = task.ExpiresAt,
            awaitingInput = task.AwaitingInputContext is not null,
            awaitingInputType = task.AwaitingInputContext?.InputType,
            awaitingQuestion = task.AwaitingInputContext?.Question,
            awaitingChoices = task.AwaitingInputContext?.Choices,
            localTaskId = task.LocalTaskId
        };
    }
}

/// <summary>Запрос на публикацию shared-факта памяти.</summary>
public sealed record SharedMemoryPublishRequest(
    string Category,
    string Content,
    List<string>? AllowedAgents = null);

/// <summary>Запрос на accept delegated задачи (task_036).</summary>
public sealed record DelegatedTaskAcceptRequest(
    string ParentRequestId,
    string CallerAgentId,
    string Intent,
    string Payload,
    Mesh.Schema.AuthContext? Auth = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>Запрос на обновление состояния delegated задачи (task_036).</summary>
public sealed record DelegatedTaskStateUpdateRequest(string State);

/// <summary>Запрос на ожидание ввода (task_036).</summary>
public sealed record AwaitInputRequest(
    string InputType,
    string? Question = null,
    List<string>? Choices = null);

/// <summary>Запрос на завершение delegated задачи (task_036).</summary>
public sealed record DelegatedTaskCompleteRequest(string Result);

/// <summary>Запрос на fail delegated задачи (task_036).</summary>
public sealed record DelegatedTaskFailRequest(string Error);

/// <summary>Запрос на cancel delegated задачи (task_036).</summary>
public sealed record DelegatedTaskCancelRequest(string? Reason = null);

/// <summary>Callback payload от вызывающего агента (task_036).</summary>
public sealed record DelegatedTaskCallbackRequest(string Input);
