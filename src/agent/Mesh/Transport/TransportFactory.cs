using System.Text;
using Hercules.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Transport;

/// <summary>
///     Фабрика транспортов: создаёт <see cref="ITransport"/> на основе конфигурации.
///     Регистрируется как singleton в DI и используется <see cref="IntentTransport"/>
///     как primary-транспорт для всех inter-agent вызовов.
///     Спецификация: task_037.
/// </summary>
public sealed class TransportFactory : ITransportFactory
{
    private readonly ICapabilityLookup _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<TransportFactory>? _logger;
    private readonly ITransport _primaryTransport;

    public TransportFactory(
        ICapabilityLookup registry,
        IServiceProvider services,
        TransportConfig config,
        ILogger<TransportFactory>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;

        _primaryTransport = ResolvePrimaryTransport(config, _services);
    }

    /// <inheritdoc />
    public ITransport Primary => _primaryTransport;

    private ITransport ResolvePrimaryTransport(TransportConfig cfg, IServiceProvider services)
    {
        // Bus transport отключён — используем HTTP или gRPC
        if (!cfg.Enabled)
        {
            _logger?.LogInformation(
                "[Transport] Bus disabled by config. Falling back to {Kind}",
                cfg.Kind);
            return CreateHttpTransport(cfg, services);
        }

        return cfg.Kind switch
        {
            TransportKind.Http => CreateHttpTransport(cfg, services),
            TransportKind.Grpc => CreateGrpcTransport(cfg, services),
            _ => CreateHttpTransport(cfg, services)
        };
    }

    private ITransport CreateHttpTransport(TransportConfig cfg, IServiceProvider services)
    {
        HttpClient? httpClient = null;
        if (services.GetService<IHttpClientFactory>() is { } hcf)
        {
            httpClient = hcf.CreateClient(nameof(HttpTransportAdapter));
        }

        // task_039: resolve peer credential provider for outbound auth
        var credentials = services.GetService<Hercules.Mesh.Auth.IPeerCredentialProvider>();

        var adapter = httpClient is not null
            ? new HttpTransportAdapter(_registry, httpClient, cfg.DefaultTimeoutMs, credentials)
            : new HttpTransportAdapter(_registry, cfg.DefaultTimeoutMs);

        adapter.EnableGzip = cfg.EnableGzip;
        _logger?.LogInformation(
            "[Transport] HTTP adapter initialised (gzip={Gzip}, auth={Auth})",
            cfg.EnableGzip, credentials is not null ? "bearer/apikey" : "none");

        return adapter;
    }

    private ITransport CreateGrpcTransport(TransportConfig cfg, IServiceProvider services)
    {
        var adapter = new GrpcTransportAdapter(_registry, cfg.DefaultTimeoutMs);
        _logger?.LogInformation("[Transport] gRPC adapter initialised");
        return adapter;
    }
}

/// <summary>
///     DI-интерфейс фабрики транспортов.
/// </summary>
public interface ITransportFactory
{
    /// <summary>Primary transport, выбранный на основе конфигурации.</summary>
    ITransport Primary { get; }
}

/// <summary>
///     Конфигурация транспорта для <see cref="MeshConfig.Transports"/>.
///     Спецификация: task_037.
/// </summary>
public sealed class TransportConfig
{
    /// <summary>Включить transport layer (false = только HTTP/gRPC, без async-bus).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Тип primary транспорта: Http | Grpc | Bus.</summary>
    public TransportKind Kind { get; set; } = TransportKind.Http;

    /// <summary>Default timeout для outbound-вызовов (мс).</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    /// <summary>Включить gzip-сжатие (только для HTTP).</summary>
    public bool EnableGzip { get; set; } = false;

    /// <summary>Bus type: "rabbitmq" | "nats" | "azure-service-bus".</summary>
    public string BusType { get; set; } = "none";

    /// <summary>Connection string для message-bus.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Имя outbound-очереди.</summary>
    public string OutboundQueue { get; set; } = "hercules.intents";

    /// <summary>Имя inbound-очереди (для подписки).</summary>
    public string InboundQueue { get; set; } = "hercules.intents.inbound";

    /// <summary>Включить TLS для bus connection.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    ///     Prioritytransport: secondary transport, используемый при сбое primary.
    ///     null = без fallback (тогда ошибка возвращается сразу).
    /// </summary>
    public TransportKind? FallbackKind { get; set; }
}

/// <summary>
///     Расширения для регистрации транспорта в DI.
/// </summary>
public static class TransportServiceExtensions
{
    /// <summary>
    ///     Зарегистрировать <see cref="TransportFactory"/> и <see cref="ITransport"/>
    ///     в DI на основе <paramref name="meshCfg"/>.
    /// </summary>
    public static IServiceCollection AddMeshTransport(
        this IServiceCollection services,
        MeshConfig meshCfg,
        IHttpClientFactory httpClientFactory)
    {
        // TransportFactory — singleton
        services.AddSingleton<ITransportFactory>(sp =>
        {
            var registry = sp.GetRequiredService<ICapabilityLookup>();
            var logger = sp.GetRequiredService<ILogger<TransportFactory>>();
            return new TransportFactory(
                registry,
                sp,
                meshCfg.Transport ?? new TransportConfig(),
                logger);
        });

        // ITransport = primary transport из factory (singleton, cached)
        services.AddSingleton<ITransport>(sp =>
            sp.GetRequiredService<ITransportFactory>().Primary);

        return services;
    }
}

