using Hercules.Agent;
using Hercules.Budget;
using Hercules.Config;
using Hercules.Fleet;
using Hercules.Mesh.A2A;
using Hercules.Mesh.Abstractions;
using Hercules.Mesh.Aggregation;
using Hercules.Mesh.Audit;
using Hercules.Mesh.Auth;
using Hercules.Mesh.Backend;
using Hercules.Mesh.Backends.Redis;
using Hercules.Mesh.Backends.Nats;
using Hercules.Mesh.Backends.Postgres;
using Hercules.Mesh.Discovery;
using Hercules.Mesh.Escalation;
using Hercules.Mesh.Eval;
using Hercules.Mesh.InProcess;
using Hercules.Mesh.Observability;
using Hercules.Mesh.Policy;
using Hercules.Mesh.Profiles;
using Hercules.Mesh.Verification;
using Hercules.Mesh.Router;
using Hercules.Mesh.TaskLifecycle;
using Hercules.Mesh.Transport;
using StackExchange.Redis;
using NATS.Client.Core;
using Npgsql;
using Hercules.Mesh.Resilience;
using Hercules.Observability;
using Hercules.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Hercules.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    public static IServiceCollection AddMeshServices(this IServiceCollection services, AppConfig appConfig, string dataRoot)
    {
        var meshCfg = appConfig.Mesh;
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
                sp.GetService<ITrustAdmissionPolicy>(),
                sp.GetService<IEscalationService>(),
                sp.GetService<IMeshObservabilityService>()));

        // Phase 3: TaskLifecycleProtocol — inter-agent task lifecycle (task_036)
        // Phase 8: persistence via SqliteDelegatedTaskStore (task_106)
        services.AddSingleton<IDelegatedTaskStore>(sp =>
            new SqliteDelegatedTaskStore(sp.GetRequiredService<Hercules.Config.StorageConfig>()));
        services.AddSingleton<ITaskLifecycleProtocol>(sp =>
        {
            var transport = sp.GetRequiredService<ITransport>();
            var logger = sp.GetRequiredService<ILogger<TaskLifecycleProtocol>>();
            var agentId = meshCfg.AgentId;
            var auditService = sp.GetService<MeshAuditService>();
            var observability = sp.GetService<IMeshObservabilityService>();
            var store = sp.GetRequiredService<IDelegatedTaskStore>();
            return new TaskLifecycleProtocol(transport, agentId, logger, auditService, observability, store);
        });

        // Phase 4: CircuitBreaker + RetryPolicy — отказоустойчивость peer-вызовов (task_047)
        // Configure from ResilienceConfig
        var resCfg = appConfig.Resilience;
        services.AddSingleton(resCfg);

        var cb = new CircuitBreaker
        {
            FailureThreshold = resCfg.CircuitBreakerFailureThreshold,
            Cooldown = TimeSpan.FromSeconds(resCfg.CircuitBreakerCooldownSeconds)
        };
        services.AddSingleton(cb);

        var rp = new RetryPolicy
        {
            MaxAttempts = resCfg.MaxAttempts,
            BaseDelay = TimeSpan.FromMilliseconds(resCfg.BaseDelayMs),
            BackoffMultiplier = resCfg.BackoffMultiplier,
            MaxDelay = TimeSpan.FromMilliseconds(resCfg.MaxDelayMs),
            JitterFactor = resCfg.JitterFactor
        };
        services.AddSingleton(rp);

        // Wrap ITransport with ResilientTransport (bulkhead + retry + CB)
        services.AddSingleton<ITransport>(sp =>
        {
            var inner = sp.GetRequiredService<ITransportFactory>().Primary;
            var logger = sp.GetRequiredService<ILogger<ResilientTransport>>();
            return new ResilientTransport(
                inner,
                sp.GetRequiredService<CircuitBreaker>(),
                sp.GetRequiredService<RetryPolicy>(),
                sp.GetRequiredService<ResilienceConfig>(),
                logger,
                sp.GetService<IMeshObservabilityService>(),
                sp.GetService<Hercules.Slo.ISloLatencyTracker>());
        });

        // Phase 4: Mesh Router (task_043) — capability-based peer routing with health + scoring
        services.AddSingleton(meshCfg.MeshRouter);
        services.AddSingleton<RouterHealthTracker>();
        services.AddSingleton<IMeshRouter, CapabilityMeshRouter>();

        // Phase 4: ResilientTransport observability (task_065) — retry/circuit spans
        // IMeshObservabilityService already registered below; ResilientTransport gets it via DI

        // Phase 4: Complexity Router (task_044) — complexity-based execution path selection
        services.AddSingleton(meshCfg.ComplexityRouter);
        services.AddSingleton<IComplexityClassifier, ComplexityClassifier>();
        services.AddSingleton<IComplexityRouter, ComplexityRouter>();

        // Phase 4: FanOut Orchestrator (task_045) — fan-out / fan-in с schema validation, voting, deterministic, LLM-judge
        services.AddSingleton(appConfig.FanOut);
        services.AddSingleton(sp => new ResponseAggregator(
            sp.GetRequiredService<FanOutOptions>(),
            sp.GetService<ILLMClient>(),
            sp.GetRequiredService<ILogger<ResponseAggregator>>()));
        services.AddSingleton<IFanOutOrchestrator>(sp =>
            new FanOutOrchestrator(
                sp.GetRequiredService<IMeshRouter>(),
                sp.GetRequiredService<ITransport>(),
                sp.GetRequiredService<ResponseAggregator>(),
                sp.GetRequiredService<FanOutOptions>(),
                sp.GetRequiredService<CircuitBreaker>(),
                sp.GetRequiredService<ILogger<FanOutOrchestrator>>(),
                sp.GetService<IMeshObservabilityService>(),
                sp.GetService<IFleetTemplateManager>()));

        // Phase 4: MeshRouter — fan-out/fan-in оркестрация с LLM-judge
        services.AddSingleton<MeshRouter>();

        // Phase 4: DistributedReflection — отчёты по mesh + рекомендации (task_050)
        services.AddSingleton(appConfig.ReflectionProposals);
        services.AddSingleton(sp => new ReflectionProposalStore(
            dataRoot,
            sp.GetRequiredService<ILogger<ReflectionProposalStore>>()));
        services.AddSingleton<DistributedReflection>();

        // Phase 4: SharedMemorySync — синхронизация избранных фактов памяти (task_051)
        services.AddSingleton(appConfig.SharedMemorySync);
        services.AddSingleton(sp => new SharedMemorySync(
            dataRoot,
            sp.GetRequiredService<CapabilityRegistry>(),
            sp.GetRequiredService<ITransport>(),
            sp.GetRequiredService<AgentManifestService>(),
            sp.GetRequiredService<SharedMemorySyncConfig>(),
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

        // Phase 4: Verification pipeline (task_046) — safety, policy, schema, numeric validators
        var verConfig = appConfig.Verification;
        services.AddSingleton(verConfig);

        if (verConfig.Enabled)
        {
            services.AddSingleton<IVerificationPipeline>(sp =>
            {
                var verifiers = new List<IVerifier>();
                if (verConfig.EnableSafetyVerifier)
                    verifiers.Add(sp.GetRequiredService<SafetyVerifier>());
                if (verConfig.EnablePolicyVerifier)
                    verifiers.Add(sp.GetRequiredService<PolicyVerifier>());
                if (verConfig.EnableSchemaVerifier)
                    verifiers.Add(sp.GetRequiredService<SchemaVerifier>());
                if (verConfig.EnableNumericValidator)
                    verifiers.Add(sp.GetRequiredService<NumericValidator>());

                return new VerificationPipeline(
                    verifiers,
                    verConfig,
                    sp.GetRequiredService<ILogger<VerificationPipeline>>());
            });
        }

        // Phase 4: Delegation boundaries (task_048) — hop count, fan-out width, cumulative tool calls, cost, time limits
        services.AddSingleton(appConfig.DelegationBoundaries);
        services.AddSingleton<IDelegationBoundaryService, DelegationBoundaryService>();

        // Phase 4: Human-in-the-loop escalation (task_049)
        services.AddSingleton(appConfig.Escalation);
        services.AddSingleton<IEscalationService, EscalationService>();

        // Phase 4: Mesh evaluation suite (task_052)
        services.AddSingleton(appConfig.MeshEval);
        services.AddSingleton<IMeshEvalRunner, MeshEvalRunner>();

        // Phase 5: Mesh dashboard (task_053) — aggregator service
        services.AddSingleton<Hercules.Mesh.Dashboard.MeshDashboardService>();

        // Phase 5: Centralized mesh observability (task_054) — trace context propagation, mesh span enrichment, OTLP metrics
        services.AddSingleton(appConfig.CentralizedObservability);
        // Phase 7: Mesh diagnostics aggregator (task_093) — in-memory counters + recent traces/logs ring buffers.
        // The diagnostics service is consumed by the controller endpoints, the activity listener and the log sink below.
        services.AddSingleton<MeshDiagnosticsService>();
        services.AddSingleton<InMemoryActivityListener>();
        // Register the in-memory log sink as both a direct service and an ILoggerProvider so the
        // ASP.NET logging pipeline picks it up alongside the JSON console provider.
        services.AddSingleton<InMemoryLogSink>(sp =>
            new InMemoryLogSink(
                sp.GetRequiredService<MeshDiagnosticsService>(),
                Microsoft.Extensions.Logging.LogLevel.Information));
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<InMemoryLogSink>());
        services.AddSingleton<IMeshObservabilityService>(sp =>
            new MeshObservabilityService(
                sp.GetRequiredService<MeshCentralizedObservabilityConfig>(),
                sp.GetRequiredService<IOtelService>(),
                sp.GetRequiredService<ILogger<MeshObservabilityService>>(),
                sp.GetRequiredService<MeshDiagnosticsService>()));

        // Phase 4: Mesh backend abstractions (task_066) — IMeshBus, ITaskQueue, IMeshStateStore
        // Default: in-process implementation (Channel-based pub/sub, ConcurrentQueue, ConcurrentDictionary)
        // Tasks 067–070 will replace these with Redis/NATS/PostgreSQL backends via profile.
        // Profile-loader is created with a NullLogger at registration time
        // (the singleton registered further down reuses the same config and
        // is resolved with the host's ILoggerFactory at first use).
        RegisterMeshBackends(services, appConfig);

        // Phase 4: Backend profiles and degradation (task_070) — profile loader and health monitor
        var meshProfilesCfg = appConfig.MeshProfiles;
        services.AddSingleton(meshProfilesCfg);
        services.AddSingleton<MeshProfileLoader>(sp =>
            new MeshProfileLoader(
                sp.GetRequiredService<MeshProfilesConfig>(),
                sp.GetRequiredService<ILogger<MeshProfileLoader>>()));
        services.AddSingleton<IMeshBackendHealthMonitor, MeshBackendHealthMonitor>();

        return services;
    }

    /// <summary>
    ///     Registers mesh backends (IMeshBus, ITaskQueue, IMeshStateStore) based on active profile.
    ///     Redis (task_067): uses Redis when Redis:Enabled or profile = Redis.
    ///     NATS (task_068): uses NATS/JetStream when Nats:Enabled or profile = Nats.
    ///     Postgres (task_069): uses Npgsql when Postgres:Enabled or profile = Postgres.
    ///     Falls back to in-process when none is available.
    /// </summary>
    private static void RegisterMeshBackends(IServiceCollection services, AppConfig appConfig)
    {
        var redisCfg = appConfig.Redis;
        var natsCfg = appConfig.Nats;
        var postgresCfg = appConfig.Postgres;
        // We only need to *read* the active profile here (a pure data lookup
        // against MeshProfilesConfig); the loader is also registered as a
        // singleton further down with a host-resolved ILogger. The NullLogger
        // here keeps this helper free of any IServiceProvider dependency, so
        // the caller no longer needs to call services.BuildServiceProvider()
        // — the root cause of the captive-dependency anti-pattern fixed in
        // task_082.
        var profileLoader = new MeshProfileLoader(appConfig.MeshProfiles, NullLogger<MeshProfileLoader>.Instance);
        var activeProfile = profileLoader.GetActiveProfile();
        var isRedisProfile = activeProfile?.Profile == MeshBackendProfile.Redis;
        var isNatsProfile = activeProfile?.Profile == MeshBackendProfile.Nats;
        var isPostgresProfile = activeProfile?.Profile == MeshBackendProfile.Postgres;
        var isRedisEnabled = redisCfg.Enabled || isRedisProfile;
        var isNatsEnabled = natsCfg.Enabled || isNatsProfile;
        var isPostgresEnabled = postgresCfg.Enabled || isPostgresProfile;

        if (isNatsEnabled)
        {
            // Register NATS connection as singleton
            services.AddSingleton<NatsConnection>(sp =>
            {
                var servers = natsCfg.Servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var primaryServer = servers.Length > 0 ? servers[0] : "nats://localhost:4222";
                var opts = new NatsOpts
                {
                    Url = primaryServer,
                };
                return new NatsConnection(opts);
            });

            services.AddSingleton(natsCfg);
            services.AddSingleton<IMeshBus, NatsMeshBus>();
            services.AddSingleton<ITaskQueue, NatsTaskQueue>();
            services.AddSingleton<IMeshStateStore, NatsMeshStateStore>();
        }
        else if (isRedisEnabled)
        {
            // Register Redis connection multiplexer (singleton per connection string)
            var redisConfig = ConfigurationOptions.Parse(redisCfg.ConnectionString);
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ConnectTimeout = 5000;
            redisConfig.SyncTimeout = 5000;

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                try
                {
                    return ConnectionMultiplexer.Connect(redisConfig);
                }
                catch
                {
                    // Return a lazy-connecting multiplexer that will gracefully degrade
                    return ConnectionMultiplexer.Connect(redisConfig);
                }
            });

            services.AddSingleton(redisCfg);
            services.AddSingleton<IMeshBus, RedisMeshBus>();
            services.AddSingleton<ITaskQueue, RedisTaskQueue>();
            services.AddSingleton<IMeshStateStore, RedisMeshStateStore>();
        }
        else if (isPostgresEnabled)
        {
            // Register Npgsql data source as singleton (task_069).
            // The data source is lazy: connection is opened only on first use.
            services.AddSingleton<NpgsqlDataSource>(sp =>
            {
                var builder = new NpgsqlDataSourceBuilder(postgresCfg.ConnectionString);
                builder.UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>());
                return builder.Build();
            });

            services.AddSingleton(postgresCfg);
            services.AddSingleton<IMeshBus, PostgresMeshBus>();
            services.AddSingleton<ITaskQueue, PostgresTaskQueue>();
            services.AddSingleton<IMeshStateStore, PostgresMeshStateStore>();
        }
        else
        {
            // Default: in-process implementations
            // task_086: pass MeshBackpressureConfig to bound concurrent handlers
            // and surface MeshBackpressure to the task queue.
            var backpressure = appConfig.Mesh.Backpressure;
            services.AddSingleton<IMeshBus>(sp => new InProcessMeshBus(
                backpressure,
                sp.GetService<ILogger<InProcessMeshBus>>()));
            services.AddSingleton<ITaskQueue>(sp => new InProcessTaskQueue(
                backpressure,
                sp.GetService<ILogger<InProcessTaskQueue>>()));
            services.AddSingleton<IMeshStateStore, InProcessMeshStateStore>();
        }
    }
}
