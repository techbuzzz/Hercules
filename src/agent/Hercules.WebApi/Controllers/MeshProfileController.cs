using Hercules.Mesh.Backend;
using Hercules.Mesh.Profiles;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     API endpoints for mesh backend profiles and health status.
///     Spec: task_070.
/// </summary>
public static class MeshProfileController
{
    public static void MapMeshProfiles(this IEndpointRouteBuilder app)
    {
        // GET /api/mesh/profiles — list all registered profile names
        app.MapGet("/api/mesh/profiles", (MeshProfileLoader loader) =>
        {
            var names = loader.ListProfileNames();
            var active = loader.GetActiveProfile();
            return Results.Ok(new
            {
                count = names.Count,
                profiles = names,
                activeProfile = active.Name
            });
        }).WithName("ListMeshProfiles");

        // GET /api/mesh/profiles/{name} — get profile definition
        app.MapGet("/api/mesh/profiles/{name}", (string name, MeshProfileLoader loader) =>
        {
            var profile = loader.GetProfile(name);
            return profile is null
                ? Results.NotFound(new { error = $"Profile '{name}' not found." })
                : Results.Ok(profile);
        }).WithName("GetMeshProfile");

        // GET /api/mesh/profiles/{name}/backends — get effective backend configs for a profile
        app.MapGet("/api/mesh/profiles/{name}/backends", (string name, MeshProfileLoader loader) =>
        {
            var profile = loader.GetProfile(name);
            if (profile is null)
                return Results.NotFound(new { error = $"Profile '{name}' not found." });

            var backends = new Dictionary<string, BackendDto>();
            foreach (var role in new[] { "bus", "queue", "stateStore" })
            {
                var cfg = loader.GetEffectiveBackend(name, role);
                backends[role] = new BackendDto
                {
                    Role = role,
                    Kind = cfg.Kind,
                    Enabled = cfg.Enabled,
                    ConnectionString = cfg.ConnectionString,
                    Hosts = cfg.Hosts,
                    HealthCheckIntervalSec = cfg.HealthCheckIntervalSec,
                    TimeoutSec = cfg.TimeoutSec,
                    MaxRetries = cfg.MaxRetries
                };
            }

            return Results.Ok(new
            {
                profile = name,
                backends
            });
        }).WithName("GetMeshProfileBackends");

        // GET /api/mesh/backend-status — live health status of all backends
        app.MapGet("/api/mesh/backend-status", (IMeshBackendHealthMonitor monitor) =>
        {
            var statuses = monitor.GetBackendStatuses();
            var summary = statuses.Count > 0
                ? statuses.All(s => s.State == BackendHealthState.Healthy) ? "Healthy"
                : statuses.Any(s => s.State == BackendHealthState.Unavailable) ? "Unavailable"
                : "Degraded"
                : "Unknown";

            return Results.Ok(new
            {
                overall = summary,
                backends = statuses.Select(s => new BackendHealthDto
                {
                    Role = s.BackendRole,
                    Kind = s.BackendKind,
                    State = s.State.ToString(),
                    LastCheckedAt = s.LastCheckedAt,
                    ConsecutiveFailures = s.ConsecutiveFailures,
                    LastError = s.LastError
                }).ToList()
            });
        }).WithName("GetMeshBackendStatus");

        // GET /api/mesh/backend-status/{role} — health status for a specific backend
        app.MapGet("/api/mesh/backend-status/{role}", (string role, IMeshBackendHealthMonitor monitor) =>
        {
            var statuses = monitor.GetBackendStatuses();
            var status = statuses.FirstOrDefault(s =>
                s.BackendRole.Equals(role, StringComparison.OrdinalIgnoreCase));

            return status is null
                ? Results.NotFound(new { error = $"Backend role '{role}' not found." })
                : Results.Ok(new BackendHealthDto
                {
                    Role = status.BackendRole,
                    Kind = status.BackendKind,
                    State = status.State.ToString(),
                    LastCheckedAt = status.LastCheckedAt,
                    ConsecutiveFailures = status.ConsecutiveFailures,
                    LastError = status.LastError
                });
        }).WithName("GetMeshBackendStatusByRole");
    }

    private sealed record BackendDto
    {
        public string Role { get; init; } = "";
        public string Kind { get; init; } = "";
        public bool Enabled { get; init; }
        public string? ConnectionString { get; init; }
        public List<string> Hosts { get; init; } = new();
        public int HealthCheckIntervalSec { get; init; }
        public int TimeoutSec { get; init; }
        public int MaxRetries { get; init; }
    }

    private sealed record BackendHealthDto
    {
        public string Role { get; init; } = "";
        public string Kind { get; init; } = "";
        public string State { get; init; } = "";
        public DateTimeOffset LastCheckedAt { get; init; }
        public int ConsecutiveFailures { get; init; }
        public string? LastError { get; init; }
    }
}
