using Hercules.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Simulation;

/// <summary>
///     Tests for FailureScenarioEngine (task_031).
///     Covers: all failure types, coverage mapping, event generation, edge cases.
/// </summary>
public class FailureScenarioEngineTests
{
    private readonly FailureScenarioEngine _engine = new(NullLogger<FailureScenarioEngine>.Instance);

    // simulationStart is used as both the reference point for timestamps and for elapsed calculation.
    private static readonly DateTimeOffset SimStartOffset = new(new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>
    ///     Creates sensor readings with ISO 8601 UTC timestamps in roundtrip format ("O").
    ///     Using "O" preserves UTC offset (+00:00) so timestamps are correctly parsed as UTC
    ///     regardless of the system's local timezone. Matches production fixture files which
    ///     use "yyyy-MM-ddTHH:mm:ssZ" (equivalent UTC representation).
    /// </summary>
    private static string UtcTs(double secondsFromStart)
        => SimStartOffset.AddSeconds(secondsFromStart).ToString("O");

    /// <summary>
    ///     Creates sensor readings with fixed-format UTC timestamps.
    /// </summary>
    private static List<SensorReading> MakeReadings(params (double sec, string sensor, double value)[] items)
        => items.Select(t => new SensorReading(
            UtcTs(t.sec),
            t.sensor,
            t.value,
            "unit",
            "loc")).ToList();

    [Fact]
    public void InjectFailures_NoReadings_ReturnsEmpty()
    {
        var result = _engine.InjectFailures([], [], SimStartOffset.UtcDateTime);
        Assert.Empty(result);
    }

    [Fact]
    public void InjectFailures_NoScenarios_ReturnsOriginalReadings()
    {
        var readings = MakeReadings((0, "temp", 20.0), (5, "temp", 21.0));
        var result = _engine.InjectFailures(readings, [], SimStartOffset.UtcDateTime);
        Assert.Equal(2, result.Count);
        Assert.Equal(20.0, result[0].Value);
        Assert.Equal(21.0, result[1].Value);
    }

    [Fact]
    public void InjectFailures_StuckAt_ReplacesValue()
    {
        var readings = MakeReadings((0, "temp", 20.0), (200, "temp", 21.0), (3100, "temp", 22.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck", "desc", "greenhouse", FailureType.StuckAt,
                ["temp"], 100, 3000, 99.9, "stuck")
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        // reading at 0s: before injection window
        Assert.Equal(20.0, result[0].Value);
        // reading at 200s: within injection window (100–3100s)
        Assert.Equal(99.9, result[1].Value);
        Assert.Contains("STUCK_AT", result[1].Status);
        // reading at 3100s: at the window boundary, still affected (3100 <= 3100)
        Assert.Equal(99.9, result[2].Value);
    }

    [Fact]
    public void InjectFailures_AfterInjectionWindow_NotAffected()
    {
        var readings = MakeReadings((0, "temp", 20.0), (200, "temp", 21.0), (4000, "temp", 22.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck", "desc", "greenhouse", FailureType.StuckAt,
                ["temp"], 100, 3000, 99.0)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value); // before 100s
        Assert.Equal(99.0, result[1].Value); // within window (100–3100s)
        Assert.Equal(22.0, result[2].Value); // after 4000s — window ended at 3100s
    }

    [Fact]
    public void InjectFailures_DataLoss_ReplacesWithNaN()
    {
        var readings = MakeReadings((0, "temp", 20.0), (200, "temp", 21.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Loss", "desc", "greenhouse", FailureType.DataLoss,
                ["temp"], 100, 3000, null, "data lost")
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value);
        Assert.True(double.IsNaN(result[1].Value));
        Assert.Contains("DATA_LOSS", result[1].Status);
    }

    [Fact]
    public void InjectFailures_Spike_AddsToValue()
    {
        var readings = MakeReadings((0, "temp", 20.0), (200, "temp", 21.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Spike", "desc", "greenhouse", FailureType.Spike,
                ["temp"], 100, 3000, 50.0, "spike")
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value);
        Assert.Equal(71.0, result[1].Value); // 21.0 + 50.0
        Assert.Contains("SPIKE", result[1].Status);
    }

    [Fact]
    public void InjectFailures_Blackout_SetsZero()
    {
        var readings = MakeReadings((0, "temp", 20.0), (200, "temp", 21.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Blackout", "desc", "greenhouse", FailureType.Blackout,
                ["temp"], 100, 3000, 0, "blackout")
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value);
        Assert.Equal(0.0, result[1].Value);
        Assert.Contains("BLACKOUT", result[1].Status);
    }

    [Fact]
    public void InjectFailures_MultipleScenarios_MultipleSensors()
    {
        var readings = MakeReadings(
            (200, "temp", 20.0),
            (200, "humidity", 65.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck1", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 100, 3000, 99.0),
            new FailureScenario("s2", "Loss1", "desc", "greenhouse", FailureType.DataLoss, ["humidity"], 100, 3000, null)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(99.0, result[0].Value);
        Assert.True(double.IsNaN(result[1].Value));
    }

    [Fact]
    public void InjectFailures_CaseInsensitive_AffectedSensors()
    {
        var readings = MakeReadings((200, "TEMP", 20.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 100, 3000, 50.0)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(50.0, result[0].Value);
    }

    [Fact]
    public void InjectFailures_DurationZero_AlwaysOnAfterInjectionPoint()
    {
        // DurationSeconds = 0 means no upper boundary: injection applies indefinitely
        // after InjectAfterSeconds (always-on from that point forward).
        var readings = MakeReadings((50, "temp", 20.0), (200, "temp", 21.0), (5000, "temp", 22.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Spike", "desc", "greenhouse", FailureType.Spike, ["temp"], 100, 0, 50.0)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        // t=50: before injection point, original value
        Assert.Equal(20.0, result[0].Value);
        // t=200: after injection point, Duration=0 = no upper bound, always injected
        Assert.Equal(71.0, result[1].Value); // 21 + 50
        // t=5000: after injection point, still injected
        Assert.Equal(72.0, result[2].Value); // 22 + 50
    }

    [Fact]
    public void InjectFailures_Latency_MarksStatus()
    {
        var readings = MakeReadings((200, "temp", 20.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Latency", "desc", "greenhouse", FailureType.Latency, ["temp"], 100, 3000, 500.0, "network degraded")
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value); // latency doesn't change value
        Assert.Contains("LATENCY", result[0].Status);
    }

    [Fact]
    public void InjectFailures_UnknownType_ReturnsOriginal()
    {
        var readings = MakeReadings((200, "temp", 20.0));
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Unknown", "desc", "greenhouse", (FailureType)999, ["temp"], 100, 3000, 50.0)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(20.0, result[0].Value);
        Assert.Null(result[0].Status);
    }

    [Fact]
    public void GenerateFailureEvents_CreatesInjectedAndResolvedEvents()
    {
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Test", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 60, 120, 50.0)
        };

        var events = _engine.GenerateFailureEvents(scenarios, SimStartOffset.UtcDateTime);

        Assert.Equal(2, events.Count);
        Assert.Equal("failure_injected", events[0].Type);
        Assert.Equal("failure_resolved", events[1].Type);
        Assert.Equal("s1", events[0].Source);
        Assert.Equal("critical", events[0].Severity);
        Assert.Equal("info", events[1].Severity);
    }

    [Fact]
    public void GenerateFailureEvents_DurationZero_NoResolvedEvent()
    {
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Spike", "desc", "greenhouse", FailureType.Spike, ["temp"], 60, 0, 50.0)
        };

        var events = _engine.GenerateFailureEvents(scenarios, SimStartOffset.UtcDateTime);

        Assert.Single(events);
        Assert.Equal("failure_injected", events[0].Type);
    }

    [Fact]
    public void GetFailureCoverage_MapsSensorsToScenarios()
    {
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck", "desc", "greenhouse", FailureType.StuckAt, ["temp", "humidity"], 60, 120, 50.0),
            new FailureScenario("s2", "Spike", "desc", "greenhouse", FailureType.Spike, ["temp"], 60, 120, 50.0),
        };

        var coverage = _engine.GetFailureCoverage(scenarios);

        Assert.Equal(2, coverage.Count);
        Assert.Equal(2, coverage["temp"].Count);
        Assert.Single(coverage["humidity"]);
        Assert.Contains("Stuck", coverage["temp"]);
        Assert.Contains("Spike", coverage["temp"]);
    }

    [Fact]
    public void InjectFailures_UnparseableTimestamp_SkipsReading()
    {
        var readings = new List<SensorReading>
        {
            new SensorReading("not-a-date", "temp", 20.0, "unit", null),
            new SensorReading("2026-08-12T00:00:00Z", "temp", 21.0, "unit", null),
        };
        var scenarios = new List<FailureScenario>
        {
            new FailureScenario("s1", "Stuck", "desc", "greenhouse", FailureType.StuckAt, ["temp"], 0, 9999, 99.0)
        };

        var result = _engine.InjectFailures(readings, scenarios, SimStartOffset.UtcDateTime);

        // First reading: unparseable, skipped (not injected)
        Assert.Equal(20.0, result[0].Value);
        // Second reading: within injection window
        Assert.Equal(99.0, result[1].Value);
    }
}
