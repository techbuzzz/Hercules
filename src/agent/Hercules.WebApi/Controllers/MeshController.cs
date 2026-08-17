using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Auth;
using Hercules.Mesh.Dashboard;
using Hercules.Mesh.Discovery;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Router;
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
        app.MapGet($"/{Hercules.BuiltIn.AgentManifestFileName}", (AgentManifestService manifestService) =>
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
            {
                return Results.NotFound(new { error = $"Агент '{id}' не найден." });
            }

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
            {
                return Results.NotFound(new { error = $"Агент '{id}' не найден." });
            }

            registry.Touch(id);
            return Results.Ok(new { status = "touched", agentId = id });
        }).WithName("TouchMeshAgent");

        // POST /api/mesh/agents/cleanup — cleanup просроченных агентов
        app.MapPost("/api/mesh/agents/cleanup", (ICapabilityRegistryService registry) =>
        {
            var removed = registry.CleanupExpired();
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
        app.MapGet("/api/mesh/capabilities", (string? agentId, CapabilityRegistry registry) => !string.IsNullOrWhiteSpace(agentId)
            ? Results.Ok((object?)registry.ListCapabilities(agentId))
            : Results.Ok((object?)registry.ListAgents())).WithName("ListMeshCapabilities");

        // GET /api/mesh/capabilities/{name} — найти агентов по имени capability
        app.MapGet("/api/mesh/capabilities/{name}", (string name, CapabilityRegistry registry) =>
            Results.Ok(registry.FindByCapability(name))).WithName("FindMeshByCapability");

        // GET /api/mesh/capabilities/search?phrase=... — semantic lookup
        app.MapGet("/api/mesh/capabilities/search", (string phrase, CapabilityRegistry registry) => string.IsNullOrWhiteSpace(phrase)
            ? Results.BadRequest(new { error = "Параметр 'phrase' обязателен." })
            : Results.Ok((object?)registry.FindByPhrase(phrase))).WithName("SearchMeshByPhrase");

        // POST /api/mesh/intent — отправить intent на маршрутизацию (IntentRouter, single-peer)
        // Trust admission policy is evaluated before routing.
        app.MapPost("/api/mesh/intent", async (
            IntentEnvelope envelope,
            IntentRouter router,
            ITrustAdmissionPolicy policy,
            CapabilityRegistry registry,
            AgentManifestService manifestService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            try
            {
                // Build trust admission context
                var callerIdentity = httpContext.GetMeshIdentity();
                var selfManifest = manifestService.Current;

                // Get target manifest if recipient is specified
                AgentManifest? targetManifest = null;
                if (!string.IsNullOrWhiteSpace(envelope.Recipient))
                {
                    targetManifest = registry.Get(envelope.Recipient);
                }

                var ctx = new TrustAdmissionContext
                {
                    CallerIdentity = callerIdentity,
                    Envelope = envelope,
                    TargetAgentId = envelope.Recipient ?? selfManifest.AgentId,
                    TargetCapabilities = targetManifest?.Capabilities,
                    TargetResourceLimits = targetManifest?.ResourceLimits,
                    TargetMinSchemaVersion = targetManifest?.SupportedProtocolVersions?.FirstOrDefault(),
                    CallerSchemaVersion = envelope.Version
                };

                var policyResult = policy.Evaluate(ctx);
                if (!policyResult.IsAllowed && !policyResult.DryRun)
                {
                    return Results.Json(new
                    {
                        error = "Trust admission denied",
                        reason = policyResult.DenialReason,
                        code = policyResult.DenialCode?.ToString()
                    }, statusCode: 403);
                }

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

            var fact = await sync.PublishFactAsync(req.Category, req.Content, req.AllowedAgents, ct: ct);
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
                {
                    return Results.BadRequest(new { error = $"Unknown state: {req.State}" });
                }

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

        // === Phase 3: Discovery mechanisms (task_038) ===

        // GET /api/mesh/discovery/sources — status of all discovery sources
        app.MapGet("/api/mesh/discovery/sources", (IDiscoveryService discovery) =>
        {
            var sources = discovery.GetSourceStatuses();
            return Results.Ok(new
            {
                count = sources.Count,
                sources = sources.Select(s => new
                {
                    source = s.Source.ToString().ToLowerInvariant(),
                    name = s.Name,
                    enabled = s.Enabled
                })
            });
        }).WithName("ListDiscoverySources");

        // GET /api/mesh/discovery/agents — all discovered agents (from cache or fresh)
        app.MapGet("/api/mesh/discovery/agents", async (
            string? source,
            IDiscoveryService discovery,
            CancellationToken ct) =>
        {
            IReadOnlyList<Hercules.Mesh.Discovery.DiscoveredAgent> agents;

            if (!string.IsNullOrWhiteSpace(source) &&
                Enum.TryParse<Hercules.Mesh.Discovery.DiscoverySourceKind>(source, true, out var sourceKind))
            {
                agents = await discovery.GetAgentsBySourceAsync(sourceKind, ct);
            }
            else
            {
                agents = await discovery.GetAgentsAsync(ct);
            }

            return Results.Ok(new
            {
                count = agents.Count,
                cacheFresh = discovery is DiscoveryService { IsCacheFresh: true },
                agents = agents.Select(a => new
                {
                    agentId = a.AgentId,
                    displayName = a.DisplayName,
                    endpoint = a.Endpoint,
                    source = a.Source.ToString().ToLowerInvariant(),
                    discoveredAt = a.DiscoveredAt,
                    capabilities = a.Capabilities,
                    manifestLoaded = a.ManifestLoaded,
                    error = a.Error
                })
            });
        }).WithName("ListDiscoveredAgents");

        // POST /api/mesh/discovery/refresh — force-refresh all discovery sources
        app.MapPost("/api/mesh/discovery/refresh", async (
            IDiscoveryService discovery,
            CancellationToken ct) =>
        {
            var agents = await discovery.RefreshAsync(ct);
            return Results.Ok(new
            {
                status = "refreshed",
                count = agents.Count,
                agents = agents.Select(a => new
                {
                    agentId = a.AgentId,
                    displayName = a.DisplayName,
                    endpoint = a.Endpoint,
                    source = a.Source.ToString().ToLowerInvariant(),
                    discoveredAt = a.DiscoveredAt,
                    capabilities = a.Capabilities,
                    manifestLoaded = a.ManifestLoaded,
                    error = a.Error
                })
            });
        }).WithName("RefreshDiscovery");

        // === Phase 3: Trust admission policy (task_040) ===

        // GET /api/mesh/policy/status — current policy configuration and mode
        app.MapGet("/api/mesh/policy/status", (
            ITrustAdmissionPolicy policy,
            TrustAdmissionConfig config) => Results.Ok(new
        {
            enabled = config.Enabled,
            mode = config.PolicyMode,
            effectiveMode = policy.Mode.ToString(),
            allowedTrustLevels = config.AllowedTrustLevels,
            allowedIntents = config.AllowedIntents,
            allowedClassifications = config.AllowedClassifications,
            allowSchemaMismatch = config.AllowSchemaMismatch,
            allowBudgetExceeded = config.AllowBudgetExceeded,
            allowedRiskLevels = config.AllowedRiskLevels
        })).WithName("GetTrustPolicyStatus");

        // POST /api/mesh/policy/dry-run — evaluate a request against the policy without enforcing
        app.MapPost("/api/mesh/policy/dry-run", (
            TrustAdmissionDryRunRequest req,
            ITrustAdmissionPolicy policy,
            CapabilityRegistry registry,
            AgentManifestService manifestService) =>
        {
            try
            {
                var selfManifest = manifestService.Current;

                // Resolve target manifest
                AgentManifest? targetManifest = null;
                if (!string.IsNullOrWhiteSpace(req.TargetAgentId))
                {
                    targetManifest = registry.Get(req.TargetAgentId);
                }

                // Build context from request
                var ctx = new TrustAdmissionContext
                {
                    CallerIdentity = req.CallerIdentity is not null
                        ? new Hercules.Mesh.Auth.IdentityResult
                        {
                            Subject = req.CallerIdentity.Subject,
                            AuthMethod = req.CallerIdentity.AuthMethod,
                            Issuer = req.CallerIdentity.Issuer,
                            Audience = req.CallerIdentity.Audience,
                            Scopes = req.CallerIdentity.Scopes ?? Array.Empty<string>(),
                            ExpiresAt = req.CallerIdentity.ExpiresAt,
                            DelegationDepth = req.CallerIdentity.DelegationDepth,
                            RootRequestId = req.CallerIdentity.RootRequestId,
                            Claims = req.CallerIdentity.Claims ?? new Dictionary<string, string>()
                        }
                        : null,
                    Envelope = req.Envelope,
                    TargetAgentId = req.TargetAgentId ?? selfManifest.AgentId,
                    TargetCapabilities = targetManifest?.Capabilities,
                    TargetResourceLimits = targetManifest?.ResourceLimits,
                    TargetMinSchemaVersion = targetManifest?.SupportedProtocolVersions?.FirstOrDefault(),
                    CallerSchemaVersion = req.CallerSchemaVersion,
                    CallerTrustLevel = ParseTrustLevel(req.CallerTrustLevel),
                    DataClassification = ParseDataClassification(req.DataClassification),
                    RequestedRiskLevel = req.RequestedRiskLevel
                };

                var result = policy.Evaluate(ctx);
                return Results.Ok(new
                {
                    allowed = result.IsAllowed,
                    denialReason = result.DenialReason,
                    denialCode = result.DenialCode?.ToString(),
                    dryRun = result.DryRun,
                    effectiveMode = policy.Mode.ToString()
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("TrustPolicyDryRun");

        // === Phase 4: Mesh Router (task_043) ===

        // GET /api/mesh/router/routes — rank peers for a given capability/intent
        app.MapGet("/api/mesh/router/routes", async (
            string capability,
            decimal? maxCostUsd,
            IMeshRouter router,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(capability))
            {
                return Results.BadRequest(new { error = "Параметр 'capability' обязателен." });
            }

            IReadOnlyList<Hercules.Mesh.Router.PeerCandidate> candidates;
            if (maxCostUsd.HasValue)
            {
                candidates = await router.RouteAsync(capability, maxCostUsd.Value, ct);
            }
            else
            {
                candidates = await router.RouteAsync(capability, ct);
            }

            return Results.Ok(new
            {
                capability,
                count = candidates.Count,
                candidates = candidates.Select(c => new
                {
                    c.AgentId,
                    c.DisplayName,
                    c.Endpoint,
                    c.HealthScore,
                    c.LatencyMs,
                    c.QualityScore,
                    c.TrustLevel,
                    c.CompositeScore,
                    c.CostHintUsd,
                    c.CircuitState,
                    c.LastSeen,
                    c.TrustPassed
                })
            });
        }).WithName("MeshRouterRoutes");

        // GET /api/mesh/router/health — health scores for all tracked peers
        app.MapGet("/api/mesh/router/health", (
            RouterHealthTracker tracker,
            CapabilityRegistry registry) =>
        {
            var agents = registry.ListAgents();
            var health = agents.ToDictionary(
                a => a.AgentId,
                a => new
                {
                    healthScore = tracker.GetHealthScore(a.AgentId),
                    avgLatencyMs = tracker.GetAverageLatencyMs(a.AgentId)
                });

            return Results.Ok(new { count = health.Count, health });
        }).WithName("MeshRouterHealth");

        // === Phase 5: Mesh Dashboard (task_053) ===

        // GET /api/mesh/dashboard — complete dashboard snapshot
        app.MapGet("/api/mesh/dashboard", async (MeshDashboardService dashboard, CancellationToken ct) =>
        {
            try
            {
                var data = await dashboard.GetDashboardAsync(ct);
                return Results.Ok(data);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500, title: "Dashboard error");
            }
        }).WithName("MeshDashboard");

        // GET /api/mesh/topology — agent list with health/trust/latency
        app.MapGet("/api/mesh/topology", async (MeshDashboardService dashboard, CancellationToken ct) =>
        {
            var data = await dashboard.GetTopologyAsync(ct);
            return Results.Ok(data);
        }).WithName("MeshTopology");

        // GET /api/mesh/health — per-agent health + circuit breaker states
        app.MapGet("/api/mesh/health", async (MeshDashboardService dashboard, CancellationToken ct) =>
        {
            var data = await dashboard.GetHealthAsync(ct);
            return Results.Ok(data);
        }).WithName("MeshHealth");

        // GET /api/mesh/denials — recent policy denials from audit log
        app.MapGet("/api/mesh/denials", async (MeshDashboardService dashboard, int limit = 50, CancellationToken ct = default) =>
        {
            var data = await dashboard.GetPolicyDenialsAsync(ct);
            return Results.Ok(data);
        }).WithName("MeshPolicyDenials");

        // GET /api/mesh/skills/heatmap — skill usage heatmap
        app.MapGet("/api/mesh/skills/heatmap", (MeshDashboardService dashboard) =>
        {
            var data = dashboard.GetSkillHeatmap();
            return Results.Ok(data);
        }).WithName("MeshSkillHeatmap");

        // GET /api/mesh/eval/summary — recent eval runs
        app.MapGet("/api/mesh/eval/summary", async (MeshDashboardService dashboard, CancellationToken ct) =>
        {
            var data = await dashboard.GetEvalSummaryAsync(ct);
            return Results.Ok(data);
        }).WithName("MeshEvalSummary");
    }

    private static TrustLevel ParseTrustLevel(string? level) =>
        Enum.TryParse<TrustLevel>(level, true, out var result) ? result : TrustLevel.Unverified;

    private static DataClassification ParseDataClassification(string? classification) =>
        Enum.TryParse<DataClassification>(classification, true, out var result)
            ? result
            : DataClassification.Public;

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

/// <summary>
///     Request для dry-run evaluation of trust admission policy (task_040).
///     Allows callers to test a policy decision without actually sending an intent.
/// </summary>
public sealed record TrustAdmissionDryRunRequest(
    IntentEnvelope Envelope,
    string? TargetAgentId = null,
    string? CallerTrustLevel = null,
    string? DataClassification = null,
    string? CallerSchemaVersion = null,
    string? RequestedRiskLevel = null,
    TrustAdmissionDryRunCallerIdentity? CallerIdentity = null);

/// <summary>
///     Caller identity for dry-run evaluation (task_040).
/// </summary>
public sealed record TrustAdmissionDryRunCallerIdentity(
    string Subject,
    string AuthMethod,
    string? Issuer = null,
    string? Audience = null,
    IReadOnlyList<string>? Scopes = null,
    DateTimeOffset? ExpiresAt = null,
    int DelegationDepth = 0,
    string? RootRequestId = null,
    Dictionary<string, string>? Claims = null);
