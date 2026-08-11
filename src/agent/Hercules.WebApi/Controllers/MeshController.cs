using Hercules.Mesh;

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

        // GET /api/mesh/agents — список известных агентов в реестре
        app.MapGet("/api/mesh/agents", (CapabilityRegistry registry) =>
        {
            var agents = registry.ListAgents();
            return Results.Ok(agents);
        }).WithName("ListMeshAgents");

        // GET /api/mesh/agents/{id} — манифест агента по ID
        app.MapGet("/api/mesh/agents/{id}", (string id, CapabilityRegistry registry) =>
        {
            var manifest = registry.Get(id);
            return manifest is null
                ? Results.NotFound(new { error = $"Агент '{id}' не найден в реестре." })
                : Results.Ok(manifest);
        }).WithName("GetMeshAgent");

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
    }
}

/// <summary>Запрос на публикацию shared-факта памяти.</summary>
public sealed record SharedMemoryPublishRequest(
    string Category,
    string Content,
    List<string>? AllowedAgents = null);
