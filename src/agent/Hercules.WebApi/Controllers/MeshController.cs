using Hercules.Mesh;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Mesh-эндпоинты: манифест агента, capability registry, inter-agent intent routing.
///     GET  /agent.manifest.json — публичный манифест агента (well-known URL для discovery).
///     GET  /api/mesh/agents — список известных агентов в реестре.
///     GET  /api/mesh/agents/{id} — манифест агента по ID.
///     POST /api/mesh/agents/register — зарегистрировать peer-агента (принимает AgentManifest).
///     DELETE /api/mesh/agents/{id} — удалить агента из реестра.
///     GET  /api/mesh/capabilities — список всех capabilities в реестре.
///     GET  /api/mesh/capabilities/{name} — найти агентов по имени capability.
///     GET  /api/mesh/capabilities/search?phrase=... — semantic lookup по фразе.
///     POST /api/mesh/intent — отправить intent на маршрутизацию (локально или peer'у).
/// </summary>
public static class MeshController
{
    public static void MapMesh(this IEndpointRouteBuilder app)
    {
        // GET /agent.manifest.json — публичный манифест (well-known URL для discovery).
        // Не требует API-key (как /api/health) — манифест публичен по спецификации mesh.
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

        // POST /api/mesh/agents/register — зарегистрировать peer-агента (принимает AgentManifest)
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

        // GET /api/mesh/capabilities — список всех capabilities указанного агента
        app.MapGet("/api/mesh/capabilities", (string? agentId, CapabilityRegistry registry) =>
        {
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                return Results.Ok(registry.ListCapabilities(agentId));
            }
            // Без agentId — список всех агентов (как /api/mesh/agents)
            return Results.Ok(registry.ListAgents());
        }).WithName("ListMeshCapabilities");

        // GET /api/mesh/capabilities/{name} — найти агентов по имени capability
        app.MapGet("/api/mesh/capabilities/{name}", (string name, CapabilityRegistry registry) =>
        {
            var agents = registry.FindByCapability(name);
            return Results.Ok(agents);
        }).WithName("FindMeshByCapability");

        // GET /api/mesh/capabilities/search?phrase=... — semantic lookup по фразе
        app.MapGet("/api/mesh/capabilities/search", (string phrase, CapabilityRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                return Results.BadRequest(new { error = "Параметр 'phrase' обязателен." });
            }
            var agents = registry.FindByPhrase(phrase);
            return Results.Ok(agents);
        }).WithName("SearchMeshByPhrase");

        // POST /api/mesh/intent — отправить intent на маршрутизацию (локально или peer'у)
        app.MapPost("/api/mesh/intent", async (IntentEnvelope envelope, IntentRouter router, CancellationToken ct) =>
        {
            try
            {
                var response = await router.RouteAsync(envelope, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Intent routing error");
            }
        }).WithName("SendMeshIntent");
    }
}