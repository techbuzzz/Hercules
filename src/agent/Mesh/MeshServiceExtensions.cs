using Hercules.Agent;
using Hercules.Config;
using Hercules.Mesh.A2A;
using Hercules.Mesh.Audit;
using Hercules.Mesh.Auth;
using Hercules.Mesh.Discovery;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Router;
using Hercules.Mesh.TaskLifecycle;
using Hercules.Mesh.Transport;
using Hercules.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh;

/// <summary>
///     Фабрика для сборки capabilities манифеста из текущих навыков агента.
///     Преобразует SkillMeta → ManifestCapability.
/// </summary>
public sealed class ManifestCapabilitiesProvider
{
    private readonly SkillManager _skills;

    public ManifestCapabilitiesProvider(SkillManager skills)
    {
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
    }

    /// <summary>
    ///     Получить текущие capabilities агента из загруженных навыков.
    ///     Каждый навык → одна capability.
    /// </summary>
    public List<ManifestCapability> GetCapabilities()
    {
        return _skills.All().Select(s => new ManifestCapability
        {
            Name = s.Meta.Id,
            Description = s.Meta.Description,
            PhraseReceivers = s.Meta.PhraseReceivers,
            Tools = s.Meta.Tools?.Select(t => t.Name).ToList()
        }).ToList();
    }

    /// <summary>
    ///     Получить расширенный список навыков агента для публикации в манифесте.
    ///     Каждый навык → ManifestSkillEntry с полной информацией.
    /// </summary>
    public List<ManifestSkillEntry> GetSkills()
    {
        return _skills.All().Select(s => new ManifestSkillEntry
        {
            Id = s.Meta.Id,
            Name = s.Meta.Name,
            Description = s.Meta.Description,
            Version = $"{s.Meta.Version}.0.0",
            RiskLevel = ((SkillRiskLevel)s.Meta.RiskLevel).ToString().ToLowerInvariant(),
            Tools = s.Meta.Tools?.Select(t => t.Name).ToList() ?? new List<string>(),
            PhraseReceivers = s.Meta.PhraseReceivers,
            CreatedAt = s.Meta.CreatedAt,
            UpdatedAt = null
        }).ToList();
    }
}

/// <summary>
///     Extension-методы для регистрации Phase 3 + Phase 4 mesh-сервисов в DI.
/// </summary>
public static class MeshServiceCollectionExtensions
{
    /// <summary>
    ///     Зарегистрировать все Phase 3 + Phase 4 mesh-сервисы:
    ///     AgentManifestService, CapabilityRegistry, ICapabilityRegistryService, IntentTransport,
    ///     IntentRouter, ManifestCapabilitiesProvider, CircuitBreaker, RetryPolicy, MeshRouter,
    ///     DistributedReflection, SharedMemorySync, CapabilityHealthService.
    /// </summary>
    public static IServiceCollection AddMeshServices(this IServiceCollection services, MeshConfig meshCfg, string dataRoot)
    {
        var registryDbPath = Path.IsPathRooted(meshCfg.RegistryDb)
            ? meshCfg.RegistryDb
            : Path.Combine(dataRoot, meshCfg.RegistryDb);

        // CapabilityRegistry — singleton с SQLite-хранилищем
        services.AddSingleton(sp => new CapabilityRegistry(registryDbPath));

        // ICapabilityRegistryService — DI-friendly обёртка
        services.AddSingleton<ICapabilityRegistryService>(sp =>
            new CapabilityRegistryService(sp.GetRequiredService<CapabilityRegistry>()));

        // CapabilityHealthService — фоновый health-check агентов
        services.AddHttpClient<CapabilityHealthService>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHostedService<CapabilityHealthService>();

        // ManifestCapabilitiesProvider — читает навыки из SkillManager
        services.AddSingleton<ManifestCapabilitiesProvider>();

        // AgentManifestService — генерирует и публикует манифест
        services.AddSingleton<AgentManifestService>(sp =>
        {
            MeshConfig meshConfig = sp.GetRequiredService<MeshConfig>();
            ManifestCapabilitiesProvider capsProvider = sp.GetRequiredService<ManifestCapabilitiesProvider>();
            var manifestDir = dataRoot;

            // Build resource limits from config
            Hercules.Mesh.ManifestResourceLimits? resourceLimits = null;
            if (meshConfig.ResourceLimits is not null)
            {
                resourceLimits = new Hercules.Mesh.ManifestResourceLimits
                {
                    MaxTokensPerRequest = meshConfig.ResourceLimits.MaxTokensPerRequest,
                    MaxConcurrentRequests = meshConfig.ResourceLimits.MaxConcurrentRequests,
                    MaxToolCallsPerRequest = meshConfig.ResourceLimits.MaxToolCallsPerRequest,
                    MaxWallClockSecondsPerRequest = meshConfig.ResourceLimits.MaxWallClockSecondsPerRequest,
                    MaxCostPerDayUsd = meshConfig.ResourceLimits.MaxCostPerDayUsd,
                    MaxTokensPerDay = meshConfig.ResourceLimits.MaxTokensPerDay
                };
            }

            // Build trust metadata from config
            Hercules.Mesh.ManifestTrustMetadata? trustMetadata = null;
            if (meshConfig.TrustMetadata is not null)
            {
                trustMetadata = new Hercules.Mesh.ManifestTrustMetadata
                {
                    Level = meshConfig.TrustMetadata.Level,
                    IdentityProvider = meshConfig.TrustMetadata.IdentityProvider,
                    VerifiedBy = meshConfig.TrustMetadata.VerifiedBy,
                    IdentityClaims = meshConfig.TrustMetadata.IdentityClaims
                };
            }

            return new AgentManifestService(
                meshConfig.AgentId,
                meshConfig.DisplayName,
                meshConfig.Description,
                meshConfig.Endpoint,
                manifestDir,
                capsProvider.GetCapabilities,
                capsProvider.GetSkills,
                "",   // primaryModel — пустой, заполняется из LLM config
                null, // fallbackModels
                meshConfig.Endpoint.TrimEnd('/') + "/api/health",
                meshConfig.SupportedProtocolVersions.Count > 0
                    ? meshConfig.SupportedProtocolVersions
                    : new List<string> { "1.0" },
                resourceLimits,
                trustMetadata);
        });

        // IntentTransport — HTTP-клиент для inter-agent вызовов (IHttpClientFactory)
        services.AddHttpClient();
        services.AddSingleton<IntentTransport>(sp =>
        {
            var registry = sp.GetRequiredService<CapabilityRegistry>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(IntentTransport));
            return new IntentTransport(registry, httpClient);
        });

        // ITransport — абстракция транспорта (HTTP / gRPC / Bus); wire via factory
        // Зависит от того, что уже зарегистрировано: CapabilityRegistry (singleton),
        // IHttpClientFactory (singleton), MeshConfig (singleton в Configure).
        services.AddSingleton<ITransportFactory>(sp =>
        {
            var registry = sp.GetRequiredService<ICapabilityLookup>();
            var logger = sp.GetRequiredService<ILogger<TransportFactory>>();
            var meshCfg = sp.GetRequiredService<MeshConfig>();
            return new TransportFactory(registry, sp, meshCfg.Transport, logger);
        });
        services.AddSingleton<ITransport>(sp =>
            sp.GetRequiredService<ITransportFactory>().Primary);

        // Phase 3: Identity & delegation (task_039) — bearer tokens, API keys, mTLS
        services.AddSingleton(meshCfg.PeerAuth);
        services.AddSingleton<TokenIssuer>(sp =>
            new TokenIssuer(
                sp.GetRequiredService<PeerAuthConfig>(),
                sp.GetRequiredService<ILogger<TokenIssuer>>()));
        services.AddSingleton<IIdentityProvider, BearerIdentityProvider>();
        services.AddSingleton<IIdentityProvider, ApiKeyIdentityProvider>();
        services.AddSingleton<IIdentityProvider, MTlsIdentityProvider>();
        services.AddSingleton<IPeerCredentialProvider>(sp =>
            new DefaultPeerCredentialProvider(
                sp.GetRequiredService<PeerAuthConfig>(),
                sp.GetRequiredService<TokenIssuer>(),
                meshCfg.AgentId,
                sp.GetRequiredService<ILogger<DefaultPeerCredentialProvider>>()));

        // Phase 3: Trust admission policy (task_040) — intent allow-lists, classification, schema, budget
        services.AddSingleton(meshCfg.TrustAdmission);
        services.AddSingleton<ITrustAdmissionPolicy>(sp =>
        {
            var cfg = sp.GetRequiredService<TrustAdmissionConfig>();
            var logger = sp.GetRequiredService<ILogger<TrustAdmissionPolicyEngine>>();

            // If Enabled=false, treat as Disabled regardless of PolicyMode string
            if (!cfg.Enabled)
            {
                logger.LogWarning("[TrustPolicy] TrustAdmissionConfig.Enabled=false — policy is disabled");
            }

            return new TrustAdmissionPolicyEngine(
                cfg,
                logger);
        });

        // IntentRouter — маршрутизация intent'ов (локально или peer'у) — Phase 3
        services.AddSingleton<IntentRouter>(sp =>
            new IntentRouter(
                sp.GetRequiredService<AgentCore>(),
                sp.GetRequiredService<CapabilityRegistry>(),
                sp.GetRequiredService<ITransport>(),
                sp.GetRequiredService<AgentManifestService>(),
                sp.GetService<MeshAuditService>(),
                sp.GetService<ITrustAdmissionPolicy>()));

        // Phase 3: TaskLifecycleProtocol — inter-agent task lifecycle (task_036)
        services.AddSingleton<ITaskLifecycleProtocol>(sp =>
        {
            var transport = sp.GetRequiredService<ITransport>();
            var logger = sp.GetRequiredService<ILogger<TaskLifecycleProtocol>>();
            var agentId = meshCfg.AgentId;
            var auditService = sp.GetService<MeshAuditService>();
            return new TaskLifecycleProtocol(transport, agentId, logger, auditService);
        });

        // Phase 4: CircuitBreaker + RetryPolicy — отказоустойчивость peer-вызовов
        services.AddSingleton<CircuitBreaker>();
        services.AddSingleton<RetryPolicy>();

        // Phase 4: Mesh Router (task_043) — capability-based peer routing with health + scoring
        services.AddSingleton(meshCfg.MeshRouter);
        services.AddSingleton<RouterHealthTracker>();
        services.AddSingleton<IMeshRouter, CapabilityMeshRouter>();

        // Phase 4: MeshRouter — fan-out/fan-in оркестрация с LLM-judge
        services.AddSingleton<MeshRouter>();

        // Phase 4: DistributedReflection — отчёты по mesh + рекомендации
        services.AddSingleton<DistributedReflection>();

        // Phase 4: SharedMemorySync — синхронизация избранных фактов памяти
        services.AddSingleton(sp => new SharedMemorySync(
            dataRoot,
            sp.GetRequiredService<CapabilityRegistry>(),
            sp.GetRequiredService<ITransport>(),
            sp.GetRequiredService<AgentManifestService>(),
            sp.GetRequiredService<ILogger<SharedMemorySync>>()));

        // Phase 3: A2A Agent Card — публикация и импорт Agent Cards
        services.AddSingleton<IAgentCardService>(sp =>
        {
            var manifestService = sp.GetRequiredService<AgentManifestService>();
            var a2aCfg = sp.GetRequiredService<A2AConfig>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(IAgentCardService));
            var logger = sp.GetRequiredService<ILogger<AgentCardService>>();
            return new AgentCardService(manifestService, a2aCfg, httpClient, logger);
        });

        // Phase 3: Discovery mechanisms (task_038) — static peers, registry, mDNS
        services.AddHttpClient<StaticDiscoverySource>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IMdnsClient, NoopMdnsClient>();

        services.AddSingleton<IDiscoverySource>(sp =>
            new StaticDiscoverySource(
                meshCfg,
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(StaticDiscoverySource)),
                meshCfg.AgentId,
                sp.GetRequiredService<ILogger<StaticDiscoverySource>>()));

        services.AddSingleton<IDiscoverySource>(sp =>
            new RegistryDiscoverySource(
                sp.GetRequiredService<CapabilityRegistry>(),
                meshCfg.AgentId,
                sp.GetRequiredService<ILogger<RegistryDiscoverySource>>()));

        services.AddSingleton<IDiscoverySource>(sp =>
            new MdnsDiscoverySource(
                sp.GetRequiredService<IMdnsClient>(),
                meshCfg.Discovery,
                sp.GetRequiredService<ILogger<MdnsDiscoverySource>>()));

        services.AddSingleton<IDiscoveryService>(sp =>
            new DiscoveryService(
                sp.GetRequiredService<IEnumerable<IDiscoverySource>>(),
                meshCfg.Discovery,
                sp.GetRequiredService<ILogger<DiscoveryService>>()));

        // Phase 3: Inter-agent audit trail (task_041) — structured log + OTel + optional file sink
        services.AddSingleton(meshCfg.InterAgentAudit);
        services.AddSingleton<IAuditSink>(sp =>
            new SerilogMeshAuditSink(
                sp.GetRequiredService<ILogger<MeshAuditService>>(),
                sp.GetRequiredService<MeshAuditConfig>()));
        services.AddSingleton<IAuditSink>(sp =>
            new OpenTelemetryMeshAuditSink(
                sp.GetRequiredService<MeshAuditConfig>()));
        if (meshCfg.InterAgentAudit.FileSinkEnabled)
        {
            services.AddSingleton<IAuditSink>(sp =>
                new FileMeshAuditSink(
                    meshCfg.InterAgentAudit.FileSinkDirectory,
                    sp.GetRequiredService<ILogger<FileMeshAuditSink>>()));
        }
        services.AddSingleton<MeshAuditService>(sp =>
            new MeshAuditService(
                sp.GetRequiredService<IEnumerable<IAuditSink>>(),
                sp.GetRequiredService<MeshAuditConfig>(),
                sp.GetRequiredService<ILogger<MeshAuditService>>()));

        return services;
    }
}
