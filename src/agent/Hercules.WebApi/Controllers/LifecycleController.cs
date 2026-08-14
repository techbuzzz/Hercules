using Hercules.Lifecycle;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Lifecycle management endpoints: inventory, start/stop/drain, health check,
///     canary deploy, update, rollback, decommissioning.
///     Specification: task_057.
/// </summary>
public static class LifecycleController
{
    public static void MapLifecycle(this IEndpointRouteBuilder app)
    {
        // GET /api/lifecycle/inventory — fleet inventory
        app.MapGet("/api/lifecycle/inventory", async (
            ILifecycleService svc,
            string? type,
            CancellationToken ct) =>
        {
            if (type == "packages")
            {
                var packages = await svc.GetSkillPackageInventoryAsync(ct);
                return Results.Ok(new
                {
                    total = packages.Count,
                    packages = packages.Select(p => new
                    {
                        packageId = p.PackageId,
                        version = p.Version,
                        state = p.State,
                        deployedAt = p.DeployedAt,
                        isCanary = p.IsCanary
                    })
                });
            }

            var agents = await svc.GetAgentInventoryAsync(ct);
            return Results.Ok(new
            {
                total = agents.Count,
                agents = agents.Select(a => new
                {
                    agentId = a.AgentId,
                    displayName = a.DisplayName,
                    state = a.State,
                    healthStatus = a.HealthStatus,
                    startedAt = a.StartedAt,
                    lastHealthCheck = a.LastHealthCheck,
                    skillPackages = a.SkillPackages
                })
            });
        }).WithName("LifecycleInventory");

        // GET /api/lifecycle/health — health check
        app.MapGet("/api/lifecycle/health", async (
            ILifecycleService svc,
            string? agentId,
            CancellationToken ct) =>
        {
            var result = await svc.CheckHealthAsync(agentId, ct);
            return Results.Ok(new
            {
                agentId = result.AgentId,
                status = result.Status,
                checkedAt = result.CheckedAt,
                latencyMs = result.LatencyMs,
                issues = result.Issues,
                details = result.Details
            });
        }).WithName("LifecycleHealth");

        // POST /api/lifecycle/agent/{agentId}/start
        app.MapPost("/api/lifecycle/agent/{agentId}/start", async (
            string agentId,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.StartAgentAsync(agentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecycleAgentStart");

        // POST /api/lifecycle/agent/{agentId}/stop
        app.MapPost("/api/lifecycle/agent/{agentId}/stop", async (
            string agentId,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.StopAgentAsync(agentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecycleAgentStop");

        // POST /api/lifecycle/agent/{agentId}/drain
        app.MapPost("/api/lifecycle/agent/{agentId}/drain", async (
            string agentId,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.DrainAgentAsync(agentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecycleAgentDrain");

        // POST /api/lifecycle/agent/{agentId}/decommission
        app.MapPost("/api/lifecycle/agent/{agentId}/decommission", async (
            string agentId,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.DecommissionAgentAsync(agentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecycleAgentDecommission");

        // POST /api/lifecycle/agent/{agentId}/rollback
        app.MapPost("/api/lifecycle/agent/{agentId}/rollback", async (
            string agentId,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.RollbackAgentAsync(agentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecycleAgentRollback");

        // POST /api/lifecycle/packages/{packageId}/update
        app.MapPost("/api/lifecycle/packages/{packageId}/update", async (
            string packageId,
            UpdatePackageRequest req,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.UpdateSkillPackageAsync(packageId, req.TargetAgentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecyclePackageUpdate");

        // POST /api/lifecycle/packages/{packageId}/canary
        app.MapPost("/api/lifecycle/packages/{packageId}/canary", async (
            string packageId,
            CanaryRequest req,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.DeployCanaryAsync(
                packageId,
                req.TargetAgentId,
                req.TrafficPercent,
                ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new { success = false, error = result.Error });
        }).WithName("LifecyclePackageCanary");

        // POST /api/lifecycle/packages/{packageId}/promote
        app.MapPost("/api/lifecycle/packages/{packageId}/promote", async (
            string packageId,
            PromoteCanaryRequest req,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.PromoteCanaryAsync(packageId, req.TargetAgentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecyclePackagePromote");

        // POST /api/lifecycle/packages/{packageId}/rollback
        app.MapPost("/api/lifecycle/packages/{packageId}/rollback", async (
            string packageId,
            RollbackPackageRequest req,
            ILifecycleService svc,
            CancellationToken ct) =>
        {
            var result = await svc.RollbackSkillPackageAsync(packageId, req.TargetAgentId, ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("LifecyclePackageRollback");
    }
}

/// <summary>Request to update a skill package on target agent(s).</summary>
public sealed record UpdatePackageRequest(string TargetAgentId);

/// <summary>Request to deploy a canary package.</summary>
public sealed record CanaryRequest(string TargetAgentId, int TrafficPercent = 10);

/// <summary>Request to promote a canary to full deployment.</summary>
public sealed record PromoteCanaryRequest(string TargetAgentId);

/// <summary>Request to rollback a skill package.</summary>
public sealed record RollbackPackageRequest(string TargetAgentId);
