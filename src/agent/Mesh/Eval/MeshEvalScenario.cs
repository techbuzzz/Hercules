namespace Hercules.Mesh.Eval;

/// <summary>
///     Тип сценария eval.
/// </summary>
public enum MeshEvalScenarioType
{
    /// <summary>Измеряет success rate агента.</summary>
    TaskSuccess,

    /// <summary>Измеряет корректность safety denials.</summary>
    SafetyDenial,

    /// <summary>Измеряет качество routing решений.</summary>
    RoutingQuality,

    /// <summary>Измеряет latency под нагрузкой.</summary>
    Latency,

    /// <summary>Измеряет стоимость выполнения.</summary>
    Cost,

    /// <summary>Измеряет resilience (retry, circuit breaker).</summary>
    Resilience,

    /// <summary>Измеряет graceful degradation при отказах.</summary>
    Degradation,

    /// <summary>Chaos-тестирование.</summary>
    Chaos
}

/// <summary>
///     Ожидаемый результат для assertion.
/// </summary>
public sealed class MeshEvalAssertion
{
    /// <summary>Имя assertion (например "success_rate", "latency_p99").</summary>
    public string Name { get; set; } = "";

    /// <summary>Ожидаемое значение (например ">=0.8", "<5000").</summary>
    public string Expected { get; set; } = "";

    /// <summary>Фактическое значение после выполнения.</summary>
    public double? Actual { get; set; }

    /// <summary>Pass/fail статус.</summary>
    public bool Passed { get; set; }
}

/// <summary>
///     Дефиниция eval-сценария.
/// </summary>
public sealed class MeshEvalScenario
{
    /// <summary>Уникальный ID сценария.</summary>
    public string Id { get; set; } = "";

    /// <summary>Читаемое имя сценария.</summary>
    public string Name { get; set; } = "";

    /// <summary>Описание что измеряет сценарий.</summary>
    public string Description { get; set; } = "";

    /// <summary>Тип сценария.</summary>
    public MeshEvalScenarioType Type { get; set; }

    /// <summary>Dependencies: IDs других сценариев, которые должны быть выполнены перед этим.</summary>
    public List<string> DependsOn { get; set; } = new();

    /// <summary>Входной intent для агента.</summary>
    public string Intent { get; set; } = "";

    /// <summary>Expected intent result (для success evaluation).</summary>
    public string? ExpectedResult { get; set; }

    /// <summary>Mock capabilities для симуляции mesh peers.</summary>
    public List<MockCapability> MockPeers { get; set; } = new();

    /// <summary>Конфигурация failure injection (для resilience/chaos).</summary>
    public FailureInjectionConfig? FailureInjection { get; set; }

    /// <summary>Assertions для проверки результата.</summary>
    public List<MeshEvalAssertion> Assertions { get; set; } = new();

    /// <summary>Metadata: tags, category, severity.</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>Версия сценария (semver).</summary>
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
///     Mock capability для симуляции mesh peer.
/// </summary>
public sealed class MockCapability
{
    /// <summary>Agent ID симулируемого peer.</summary>
    public string AgentId { get; set; } = "";

    /// <summary>Endpoint симулируемого peer.</summary>
    public string Endpoint { get; set; } = "http://localhost:9999";

    /// <summary>Capabilities этого peer.</summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>Latency в мс для симуляции.</summary>
    public int SimulatedLatencyMs { get; set; } = 100;

    /// <summary>Failure rate 0.0-1.0.</summary>
    public double FailureRate { get; set; } = 0.0;
}

/// <summary>
///     Конфигурация failure injection для resilience/chaos сценариев.
/// </summary>
public sealed class FailureInjectionConfig
{
    /// <summary>Тип failure: "timeout", "connection_reset", "schema_mismatch", "peer_unavailable".</summary>
    public string Type { get; set; } = "timeout";

    /// <summary>Вероятность injection (0.0-1.0).</summary>
    public double Rate { get; set; } = 0.1;

    /// <summary>Delay в мс перед failure (для timeout).</summary>
    public int DelayMs { get; set; } = 1000;

    /// <summary>Peer для которого делаем injection (empty = all).</summary>
    public string? TargetPeer { get; set; }
}
