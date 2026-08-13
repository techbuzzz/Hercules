using Hercules.Agent;
using Hercules.Config;
using Hercules.Mesh.A2A;
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
    ///     AgentManifestService, CapabilityRegistry, IntentTransport, IntentRouter,
    ///     ManifestCapabilitiesProvider, CircuitBreaker, RetryPolicy, MeshRouter,
    ///     DistributedReflection, SharedMemorySync.
    /// </summary>
    public static IServiceCollection AddMeshServices(this IServiceCollection services, MeshConfig meshCfg, string dataRoot)
    {
        var registryDbPath = Path.IsPathRooted(meshCfg.RegistryDb)
            ? meshCfg.RegistryDb
            : Path.Combine(dataRoot, meshCfg.RegistryDb);

        // CapabilityRegistry — singleton с SQLite-хранилищем
        services.AddSingleton(sp => new CapabilityRegistry(registryDbPath));

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

        // IntentRouter — маршрутизация intent'ов (локально или peer'у) — Phase 3
        services.AddSingleton<IntentRouter>();

        // Phase 4: CircuitBreaker + RetryPolicy — отказоустойчивость peer-вызовов
        services.AddSingleton<CircuitBreaker>();
        services.AddSingleton<RetryPolicy>();

        // Phase 4: MeshRouter — fan-out/fan-in оркестрация с LLM-judge
        services.AddSingleton<MeshRouter>();

        // Phase 4: DistributedReflection — отчёты по mesh + рекомендации
        services.AddSingleton<DistributedReflection>();

        // Phase 4: SharedMemorySync — синхронизация избранных фактов памяти
        services.AddSingleton(sp => new SharedMemorySync(
            dataRoot,
            sp.GetRequiredService<CapabilityRegistry>(),
            sp.GetRequiredService<IntentTransport>(),
            sp.GetRequiredService<AgentManifestService>()));

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

        return services;
    }
}
