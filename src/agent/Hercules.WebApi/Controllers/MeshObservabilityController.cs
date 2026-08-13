using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Router;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Mesh observability endpoints: status, config, metrics summary.
///     Specification: docs/roadmap/tasks/task_065.md.
/// </summary>
public static class MeshObservabilityController
{
    public static void MapMeshObservability(this IEndpointRouteBuilder app)
    {
        // GET /api/mesh/observability/status — current mesh observability status
        app.MapGet("/api/mesh/observability/status", (
            IMeshObservabilityService observability,
            MeshCentralizedObservabilityConfig config) =>
        {
            return Results.Ok(new
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
                }
            });
        }).WithName("MeshObservabilityStatus")
          .WithTags("Mesh.Observability");

        // GET /api/mesh/observability/config — raw config
        app.MapGet("/api/mesh/observability/config", (
            IMeshObservabilityService observability) =>
        {
            return Results.Ok(new
            {
                enabled = observability.IsEnabled
            });
        }).WithName("MeshObservabilityConfig")
          .WithTags("Mesh.Observability");
    }
}
