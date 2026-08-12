using Hercules.Config;
using Hercules.Observability;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты OpenTelemetry telemetry status (task_013).
/// </summary>
public static class ObservabilityController
{
    public static void MapObservability(this IEndpointRouteBuilder app)
    {
        // GET /api/observability/telemetry — текущий статус OTel-конфигурации
        app.MapGet("/api/observability/telemetry", (IOtelService otel, OtelConfig config) =>
            Results.Ok(new
            {
                enabled = otel.IsEnabled,
                serviceName = config.ServiceName,
                otlpEndpoint = config.OtlpEndpoint,
                samplingRatio = config.SamplingRatio,
                activitySourceName = OtelSetup.ServiceName,
                meterName = OtelSetup.ServiceName
            }))
            .WithName("ObservabilityTelemetry");
    }
}
