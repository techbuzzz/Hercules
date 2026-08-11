using Hercules.Agent;
using Hercules.Config;
using Microsoft.Extensions.DependencyInjection;

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

            return new AgentManifestService(
                meshConfig.AgentId,
                meshConfig.DisplayName,
                meshConfig.Description,
                meshConfig.Endpoint,
                manifestDir,
                capsProvider.GetCapabilities,
                "",
                null,
                meshConfig.Endpoint.TrimEnd('/') + "/api/health");
        });

        // IntentTransport — HTTP-клиент для inter-agent вызовов
        services.AddSingleton<IntentTransport>();

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
        services.AddSingleton<SharedMemorySync>();

        return services;
    }
}
