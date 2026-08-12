using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hercules.Observability;

/// <summary>
///     OpenTelemetry setup: shared ActivitySource and Meter instances.
///     Created once per process — used by OtelMetrics and instrumented services.
/// </summary>
public static class OtelSetup
{
    /// <summary>Название источника трассировки. Используется в ActivitySource.Start() и source.Name.</summary>
    public const string ServiceName = "hercules";

    /// <summary>Версия схемы OTel для совместимости инструментария.</summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    ///     ActivitySource для Hercules — все трассировки (agent loop, LLM, tools, storage).
    ///     Используется через OtelService чтобы поддерживать graceful no-op при выключенном OTel.
    /// </summary>
    public static readonly ActivitySource Source = new(ServiceName, ServiceVersion);

    /// <summary>
    ///     Meter для Hercules — все метрики (counters, histograms, gauges).
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);
}
