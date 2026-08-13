namespace Hercules.Simulation;

/// <summary>
///     Sensor reading fixture: a single timestamped data point for simulation.
/// </summary>
public sealed record SensorReading(
    /// <summary>ISO 8601 timestamp.</summary>
    string Timestamp,
    /// <summary>Sensor name (e.g. "temperature", "humidity").</summary>
    string Sensor,
    /// <summary>Numeric value.</summary>
    double Value,
    /// <summary>Unit string (e.g. "°C", "%", "ppm").</summary>
    string Unit,
    /// <summary>Optional location tag.</summary>
    string? Location = null,
    /// <summary>Sensor metadata (e.g. status).</summary>
    string? Status = null);

/// <summary>
///     Event fixture: a single timestamped event for simulation replay.
/// </summary>
public sealed record EventFixture(
    string Timestamp,
    string Type,
    string Description,
    string Severity,
    string? Payload = null,
    string? Source = null);

/// <summary>
///     Failure scenario: describes a fault to inject into simulation.
/// </summary>
public sealed record FailureScenario(
    string Id,
    string Name,
    string Description,
    string Scenario,
    FailureType Type,
    string[] AffectedSensors,
    double InjectAfterSeconds,
    double DurationSeconds,
    double? FaultValue,
    string? FaultMessage = null);

/// <summary>
///     Type of failure to inject.
/// </summary>
public enum FailureType
{
    /// <summary>Sensor reports constant or unrealistic value.</summary>
    StuckAt,
    /// <summary>Sensor data becomes unavailable.</summary>
    DataLoss,
    /// <summary>Sensor reports a spike/drop anomaly.</summary>
    Spike,
    /// <summary>Sensor readings become noisy.</summary>
    Noise,
    /// <summary>Network/dispatch latency injection.</summary>
    Latency,
    /// <summary>Complete sensor blackout.</summary>
    Blackout,
}

/// <summary>
///     Result of running a simulation: sensor data, events, and metadata.
/// </summary>
public sealed record SimulationResult(
    string TemplateName,
    string Scenario,
    List<SensorReading> Readings,
    List<EventFixture> Events,
    List<AppliedFailure> AppliedFailures,
    SimulationMetrics Metrics);

/// <summary>
///     A failure that was applied during simulation.
/// </summary>
public sealed record AppliedFailure(
    string FailureId,
    string FailureName,
    FailureType Type,
    DateTime InjectedAt,
    DateTime ResolvedAt,
    bool Resolved);

/// <summary>
///     Metrics collected during a simulation run.
/// </summary>
public sealed record SimulationMetrics(
    int TotalReadings,
    int TotalEvents,
    int FailuresInjected,
    int FailuresResolved,
    double ElapsedSeconds,
    DateTime StartedAt,
    DateTime CompletedAt);

/// <summary>
///     Simulation session: contains loaded fixtures and active failure state.
/// </summary>
public sealed class SimulationSession
{
    public string TemplateName { get; init; } = "";
    public string Scenario { get; init; } = "";
    public List<SensorReading> SensorFixtures { get; init; } = new();
    public List<EventFixture> EventFixtures { get; init; } = new();
    public List<FailureScenario> FailureScenarios { get; init; } = new();
    public List<AppliedFailure> AppliedFailures { get; } = new();
    public List<SensorReading> GeneratedReadings { get; } = new();
    public List<EventFixture> GeneratedEvents { get; } = new();
    public bool IsRunning { get; set; }
    public DateTime? StartedAt { get; set; }
}

/// <summary>
///     Replay mode for sensor data.
/// </summary>
public enum ReplayMode
{
    /// <summary>Return raw fixtures as-is.</summary>
    Raw,
    /// <summary>Interpolate values between fixtures.</summary>
    Interpolated,
    /// <summary>Inject failures on top of fixtures.</summary>
    WithFailures,
}
