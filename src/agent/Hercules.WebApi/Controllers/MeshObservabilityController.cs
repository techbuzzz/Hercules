using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Router;
using Microsoft.AspNetCore.Http;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Mesh observability endpoints: status, config, counters, recent traces, recent logs.
///     Spec: docs/roadmap/tasks/completed/task_065.md (base status/config) +
///           docs/roadmap/tasks/task_093.md (counters/traces/logs).
/// </summary>
public static class MeshObservabilityController
{
    /// <summary>Hard cap on the size of <c>?limit=</c> queries for the trace and log endpoints.</summary>
    public const int MaxRecentItems = 200;

    public static void MapMeshObservability(this IEndpointRouteBuilder app)
    {
        // GET /api/mesh/observability/status — current mesh observability status + counters
        app.MapGet("/api/mesh/observability/status", (
            IMeshObservabilityService observability,
            MeshCentralizedObservabilityConfig config,
            MeshDiagnosticsService diagnostics) => Results.Ok(new
        {
            enabled = observability.IsEnabled,
            config = new
            {
                enabled = config.Enabled,
                propagationFormat = config.PropagationFormat,
                enableSpanEnrichment = config.EnableSpanEnrichment,
                enableMetrics = config.EnableMetrics,
                enableStructuredLogs = config.EnableStructuredLogs,
                enableTraceContextPropagation = config.EnableTraceContextPropagation,
                enableTraceContextExtraction = config.EnableTraceContextExtraction,
                maxTagValueLength = config.MaxTagValueLength,
                redactedAttributes = config.RedactedAttributes,
                otlpEndpoints = config.OtlpEndpoints
            },
            counters = BuildCounters(diagnostics.Snapshot())
        })).WithName("MeshObservabilityStatus")
          .WithTags("Mesh.Observability");

        // GET /api/mesh/observability/config — raw config
        app.MapGet("/api/mesh/observability/config", (
            IMeshObservabilityService observability) => Results.Ok(new
        {
            enabled = observability.IsEnabled
        })).WithName("MeshObservabilityConfig")
          .WithTags("Mesh.Observability");

        // GET /api/mesh/observability/counters — totals + by-capability + by-peer
        app.MapGet("/api/mesh/observability/counters", (MeshDiagnosticsService diagnostics) =>
        {
            var snapshot = diagnostics.Snapshot();
            return Results.Ok(BuildCounters(snapshot));
        }).WithName("MeshObservabilityCounters")
          .WithTags("Mesh.Observability");

        // GET /api/mesh/observability/traces?limit=N — recent completed traces
        app.MapGet("/api/mesh/observability/traces", (
            int? limit,
            MeshDiagnosticsService diagnostics) =>
        {
            var effectiveLimit = NormalizeLimit(limit);
            var traces = diagnostics.RecentTraces(effectiveLimit);
            return Results.Ok(new
            {
                count = traces.Count,
                limit = effectiveLimit,
                traces = traces.Select(t => new
                {
                    traceId = t.TraceId,
                    rootName = t.RootName,
                    startedAt = t.StartedAt,
                    durationMs = t.DurationMs,
                    status = t.Status,
                    spanCount = t.SpanCount
                }).ToList()
            });
        }).WithName("MeshObservabilityTraces")
          .WithTags("Mesh.Observability");

        // GET /api/mesh/observability/logs?limit=N&level=info — recent log entries
        app.MapGet("/api/mesh/observability/logs", (
            int? limit,
            string? level,
            MeshDiagnosticsService diagnostics) =>
        {
            var effectiveLimit = NormalizeLimit(limit);
            var minLevel = ParseLevel(level);
            var logs = diagnostics.RecentLogs(effectiveLimit, minLevel);
            return Results.Ok(new
            {
                count = logs.Count,
                limit = effectiveLimit,
                level = minLevel?.ToString() ?? "all",
                logs = logs.Select(l => new
                {
                    timestamp = l.Timestamp,
                    level = l.Level,
                    source = l.Source,
                    requestId = l.RequestId,
                    traceId = l.TraceId,
                    message = l.Message,
                    structuredFields = l.StructuredFields
                }).ToList()
            });
        }).WithName("MeshObservabilityLogs")
          .WithTags("Mesh.Observability");
    }

    private static int NormalizeLimit(int? limit)
    {
        if (!limit.HasValue || limit.Value <= 0) return MeshDiagnosticsService.MaxTraces;
        return Math.Min(limit.Value, MaxRecentItems);
    }

    private static LogLevel? ParseLevel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" or "fatal" => LogLevel.Critical,
            _ => null
        };
    }

    private static object BuildCounters(MeshDiagnosticsSnapshot snapshot) => new
    {
        from = snapshot.From,
        to = snapshot.To,
        routingDecision = snapshot.RoutingDecisionCount,
        retryAttempt = snapshot.RetryAttemptCount,
        circuitBreakerStateChange = snapshot.CircuitBreakerStateChangeCount,
        delegation = snapshot.DelegationCount,
        meshBackendHealth = snapshot.MeshBackendHealthCount,
        byCapability = snapshot.ByCapability,
        byPeer = snapshot.ByPeer
    };
}
