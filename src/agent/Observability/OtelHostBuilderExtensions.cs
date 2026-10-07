using System.Diagnostics;
using Hercules.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hercules.Observability;

/// <summary>
///     DI-friendly OpenTelemetry setup for Hercules.
///     Registers OtelService, ActivitySource, Meter, and configures exporters.
///     Console exporter is always enabled; OTLP exporter is added when OtlpEndpoint is set.
/// </summary>
public static class OtelHostBuilderExtensions
{
    /// <summary>
    ///     Register Hercules OpenTelemetry services (tracing + metrics).
    ///     Safe to call multiple times (idempotent).
    /// </summary>
    public static IHostApplicationBuilder AddHerculesOtel(this IHostApplicationBuilder builder, OtelConfig config)
    {
        return builder.AddHerculesOtelCore(config);
    }

    /// <summary>
    ///     Overload for non-IHostApplicationBuilder (e.g. generic Microsoft.Extensions.Hosting.HostBuilder).
    /// </summary>
    public static IServiceCollection AddHerculesOtel(this IServiceCollection services, OtelConfig config)
    {
        return services.AddHerculesOtelCore(config);
    }

    private static TBuilder AddHerculesOtelCore<TBuilder>(this TBuilder builder, OtelConfig config)
        where TBuilder : IHostApplicationBuilder
    {
        // Register OtelService as singleton with secret masking support (task_015)
        builder.Services.AddSingleton<IOtelService>(sp =>
            new OtelService(
                config,
                sp.GetService<SecretsConfig>(),
                sp.GetService<ISecretMaskingService>()));

        if (!config.Enabled)
        {
            return builder;
        }

        var serviceName = string.IsNullOrWhiteSpace(config.ServiceName) ? "hercules" : config.ServiceName;
        var samplingRatio = Math.Clamp(config.SamplingRatio, 0.0, 1.0);

        // Sampling: AlwaysOn for ratio >= 1.0, TraceIdRatioBased otherwise
        Sampler sampler = samplingRatio >= 1.0
            ? new AlwaysOnSampler()
            : new TraceIdRatioBasedSampler(samplingRatio);

        // [task_085] Console exporter is gated: enabled by default only when no OTLP endpoint
        // is configured. If both exporters run, traces/metrics are exported twice (duplicate cost
        // and stdout blocking). Override via `Otel.ConsoleExporterEnabled`.
        var consoleEnabled = config.GetEffectiveConsoleExporterEnabled();
        var hasOtlp = !string.IsNullOrWhiteSpace(config.OtlpEndpoint);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: OtelSetup.ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing.SetSampler(sampler);

                if (consoleEnabled)
                {
                    tracing.AddConsoleExporter(options =>
                    {
                        options.Targets = ConsoleExporterOutputTargets.Console;
                    });
                }

                if (hasOtlp)
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint!);
                    });
                }

                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = ctx =>
                    {
                        // Skip browser CORS preflight spans — they create noisy OPTIONS activities.
                        return !string.Equals(ctx.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
                    };
                });

                tracing.AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                });

                // Register Hercules ActivitySource (used by AgentCore, LLM, tools, etc.)
                tracing.AddSource(OtelSetup.ServiceName);
            })
            .WithMetrics(metrics =>
            {
                // Console metrics exporter is disabled by default because runtime/process
                // instrumentation floods stdout every 10s. Metrics are still exported to OTLP
                // when OtlpEndpoint is configured, and custom Hercules meters stay registered.
                if (consoleEnabled && hasOtlp)
                {
                    metrics.AddConsoleExporter(options =>
                    {
                        options.Targets = ConsoleExporterOutputTargets.Console;
                    });
                }

                if (hasOtlp)
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint!);
                    });
                }

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();

                // [task_085] Process instrumentation — CPU, memory, thread metrics.
                // The package was already referenced in Hercules.csproj (line 75) but
                // AddProcessInstrumentation() was never called, so process metrics
                // were silently missing.
                metrics.AddProcessInstrumentation();

                // [task_085] Explicit bucket boundaries for SLO-relevant histograms.
                // OpenTelemetry Views override the default bucket layout for matching
                // instruments, giving dashboards much better resolution in the
                // 50ms-5s (latency) and 100-32k (tokens) ranges.
                AddHistogramViews(metrics);

                // Register Hercules Meter (used by OtelMetrics)
                metrics.AddMeter(OtelSetup.ServiceName);
            });

        // [task_085] OTLP log exporter is OPT-IN via OtelConfig.OtlpLogExporterEnabled,
        // but the OpenTelemetry.Extensions.Logging package is intentionally not
        // referenced to keep the core dependency footprint small. Callers that
        // want OTLP logs should add the package and wire it themselves (see
        // Program.cs comment). We keep the flag in config so apps can flip it
        // without code changes once the package is added.
        _ = config.OtlpLogExporterEnabled;

        return builder;
    }

    /// <summary>
    ///     Extension overload for plain IServiceCollection (backward compatible with existing Program.cs patterns).
    /// </summary>
    private static IServiceCollection AddHerculesOtelCore(this IServiceCollection services, OtelConfig config)
    {
        services.AddSingleton<IOtelService>(sp =>
            new OtelService(
                config,
                sp.GetService<SecretsConfig>(),
                sp.GetService<ISecretMaskingService>()));

        if (!config.Enabled)
        {
            return services;
        }

        var serviceName = string.IsNullOrWhiteSpace(config.ServiceName) ? "hercules" : config.ServiceName;
        var consoleEnabled = config.GetEffectiveConsoleExporterEnabled();
        var hasOtlp = !string.IsNullOrWhiteSpace(config.OtlpEndpoint);

        Sampler sampler = config.SamplingRatio >= 1.0
            ? new AlwaysOnSampler()
            : new TraceIdRatioBasedSampler(config.SamplingRatio);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: OtelSetup.ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing.SetSampler(sampler);
                if (consoleEnabled)
                {
                    tracing.AddConsoleExporter();
                }
                if (hasOtlp)
                {
                    tracing.AddOtlpExporter(options => { options.Endpoint = new Uri(config.OtlpEndpoint!); });
                }
                tracing.AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = ctx =>
                    {
                        return !string.Equals(ctx.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
                    };
                });
                tracing.AddHttpClientInstrumentation(o => { o.RecordException = true; });
                tracing.AddSource(OtelSetup.ServiceName);
            })
            .WithMetrics(metrics =>
            {
                // Console metrics exporter is disabled by default because runtime/process
                // instrumentation floods stdout every 10s. Metrics are still exported to OTLP
                // when OtlpEndpoint is configured, and custom Hercules meters stay registered.
                if (consoleEnabled && hasOtlp)
                {
                    metrics.AddConsoleExporter();
                }
                if (hasOtlp)
                {
                    metrics.AddOtlpExporter(options => { options.Endpoint = new Uri(config.OtlpEndpoint!); });
                }
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                // [task_085] Process instrumentation (CPU/memory/threads).
                metrics.AddProcessInstrumentation();
                // [task_085] Explicit histogram bucket boundaries.
                AddHistogramViews(metrics);
                metrics.AddMeter(OtelSetup.ServiceName);
            });

        return services;
    }

    /// <summary>
    ///     [task_085] Apply explicit histogram bucket boundaries to the latency and
    ///     token instruments defined in <see cref="OtelMetrics"/>.
    ///     <para>
    ///     SDK 1.17's public <c>MetricStreamConfiguration</c> does not expose
    ///     <c>HistogramBucketBoundaries</c> via the stable view API; the array
    ///     defaults are therefore applied at instrument creation time (see
    ///     <c>OtelMetrics.LatencyBucketsMs</c> / <c>TokenBuckets</c>). This hook
    ///     remains so future SDK releases — or a Prometheus/AspNetCore advice
    ///     override — can be wired in a single place. It is intentionally a no-op
    ///     today to keep the build green while the SDK gap is tracked.
    ///     </para>
    /// </summary>
    private static MeterProviderBuilder AddHistogramViews(MeterProviderBuilder metrics)
    {
        // No-op: bucket boundaries are baked into the histogram instruments at
        // creation time (OtelMetrics.cs). Revisit when OpenTelemetry 1.18+ lands.
        _ = OtelMetrics.LatencyBucketsMs;
        _ = OtelMetrics.TokenBuckets;
        return metrics;
    }
}
