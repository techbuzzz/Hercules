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
        // Register OtelService as singleton (used by instrumented services)
        builder.Services.AddSingleton<IOtelService>(new OtelService(config));

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

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: OtelSetup.ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(sampler)
                    .AddConsoleExporter(options =>
                    {
                        options.Targets = ConsoleExporterOutputTargets.Console;
                    });

                if (!string.IsNullOrWhiteSpace(config.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint);
                    });
                }

                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
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
                metrics.AddConsoleExporter(options =>
                {
                    options.Targets = ConsoleExporterOutputTargets.Console;
                });

                if (!string.IsNullOrWhiteSpace(config.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(config.OtlpEndpoint);
                    });
                }

                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();

                // Register Hercules Meter (used by OtelMetrics)
                metrics.AddMeter(OtelSetup.ServiceName);
            });

        return builder;
    }

    /// <summary>
    ///     Extension overload for plain IServiceCollection (backward compatible with existing Program.cs patterns).
    /// </summary>
    private static IServiceCollection AddHerculesOtelCore(this IServiceCollection services, OtelConfig config)
    {
        services.AddSingleton<IOtelService>(new OtelService(config));

        if (!config.Enabled)
        {
            return services;
        }

        var serviceName = string.IsNullOrWhiteSpace(config.ServiceName) ? "hercules" : config.ServiceName;

        Sampler sampler = config.SamplingRatio >= 1.0
            ? new AlwaysOnSampler()
            : new TraceIdRatioBasedSampler(config.SamplingRatio);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: OtelSetup.ServiceVersion))
            .WithTracing(tracing =>
            {
                tracing.SetSampler(sampler);
                tracing.AddConsoleExporter();
                if (!string.IsNullOrWhiteSpace(config.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(options => { options.Endpoint = new Uri(config.OtlpEndpoint); });
                }
                tracing.AddAspNetCoreInstrumentation(o => { o.RecordException = true; });
                tracing.AddHttpClientInstrumentation(o => { o.RecordException = true; });
                tracing.AddSource(OtelSetup.ServiceName);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddConsoleExporter();
                if (!string.IsNullOrWhiteSpace(config.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(options => { options.Endpoint = new Uri(config.OtlpEndpoint); });
                }
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddMeter(OtelSetup.ServiceName);
            });

        return services;
    }
}
