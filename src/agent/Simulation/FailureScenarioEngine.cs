using Microsoft.Extensions.Logging;

namespace Hercules.Simulation;

/// <summary>
///     Injects failures into sensor data streams.
///     All mutations are non-destructive: original fixture values are preserved.
/// </summary>
public sealed class FailureScenarioEngine(ILogger<FailureScenarioEngine> logger)
{
    /// <summary>
    ///     Inject failures into a list of sensor readings based on active scenarios.
    ///     Returns a new list with failures applied (original list unchanged).
    /// </summary>
    public List<SensorReading> InjectFailures(
        IReadOnlyList<SensorReading> readings,
        IReadOnlyList<FailureScenario> scenarios,
        DateTime simulationStart)
    {
        if (readings.Count == 0 || scenarios.Count == 0)
        {
            return [.. readings];
        }

        var result = new List<SensorReading>(readings.Count);

        foreach (var reading in readings)
        {
            if (!TryParseTimestamp(reading.Timestamp, simulationStart, out var elapsed))
            {
                result.Add(reading);
                continue;
            }
            SensorReading modified = reading;

            foreach (var scenario in scenarios)
            {
                if (!scenario.AffectedSensors.Contains(reading.Sensor, StringComparer.OrdinalIgnoreCase))
                    continue;

                // Check if within injection window
                if (elapsed < scenario.InjectAfterSeconds)
                    continue;

                // DurationSeconds = 0 means always-on after injectAfterSeconds (infinite window)
                if (scenario.DurationSeconds > 0 && elapsed > scenario.InjectAfterSeconds + scenario.DurationSeconds)
                    continue;

                modified = InjectSingleFailure(modified, scenario, simulationStart.AddSeconds(elapsed), simulationStart);
                logger.LogDebug(
                    "Injected {Type} failure '{Name}' on sensor {Sensor} at {Timestamp}",
                    scenario.Type, scenario.Name, reading.Sensor, reading.Timestamp);
            }

            result.Add(modified);
        }

        return result;
    }

    /// <summary>
    ///     Filter events based on failure injection window.
    ///     Can generate synthetic failure events from failure scenarios.
    /// </summary>
    public List<EventFixture> GenerateFailureEvents(
        IReadOnlyList<FailureScenario> scenarios,
        DateTime simulationStart)
    {
        var events = new List<EventFixture>();

        foreach (var scenario in scenarios)
        {
            var injectedAt = simulationStart.AddSeconds(scenario.InjectAfterSeconds);
            var resolvedAt = injectedAt.AddSeconds(scenario.DurationSeconds);

            events.Add(new EventFixture(
                Timestamp: injectedAt.ToString("O"),
                Type: "failure_injected",
                Description: $"{scenario.Type}: {scenario.Name} — {scenario.Description}",
                Severity: "critical",
                Payload: null,
                Source: scenario.Id));

            if (scenario.DurationSeconds > 0)
            {
                events.Add(new EventFixture(
                    Timestamp: resolvedAt.ToString("O"),
                    Type: "failure_resolved",
                    Description: $"Failure '{scenario.Name}' resolved after {scenario.DurationSeconds:F1}s",
                    Severity: "info",
                    Payload: null,
                    Source: scenario.Id));
            }
        }

        return events;
    }

    /// <summary>
    ///     Get failure coverage — which sensors are affected by which scenarios.
    /// </summary>
    public Dictionary<string, List<string>> GetFailureCoverage(
        IReadOnlyList<FailureScenario> scenarios)
    {
        var coverage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in scenarios)
        {
            foreach (var sensor in scenario.AffectedSensors)
            {
                if (!coverage.ContainsKey(sensor))
                    coverage[sensor] = [];
                coverage[sensor].Add(scenario.Name);
            }
        }

        return coverage;
    }

    private SensorReading InjectSingleFailure(
        SensorReading reading,
        FailureScenario scenario,
        DateTime timestamp,
        DateTime simulationStart)
    {
        var elapsed = (timestamp - simulationStart).TotalSeconds;

        return scenario.Type switch
        {
            FailureType.StuckAt => reading with
            {
                Value = scenario.FaultValue ?? reading.Value,
                Status = $"STUCK_AT: {scenario.FaultMessage ?? reading.Value.ToString()}",
            },

            FailureType.DataLoss => reading with
            {
                Value = double.NaN,
                Status = "DATA_LOSS",
            },

            FailureType.Spike => reading with
            {
                Value = reading.Value + (scenario.FaultValue ?? reading.Value * 0.5),
                Status = $"SPIKE: {scenario.FaultMessage ?? "anomaly detected"}",
            },

            FailureType.Noise => reading with
            {
                Value = reading.Value + (Random.Shared.NextDouble() * 2.0 - 1.0) * (Math.Abs(reading.Value) * 0.1 + 1.0),
                Status = "NOISE_INJECTED",
            },

            FailureType.Latency => reading with
            {
                // Latency is a metadata concern; mark it in status
                Status = $"LATENCY: {scenario.FaultValue:F0}ms delay — {scenario.FaultMessage ?? "network degraded"}",
            },

            FailureType.Blackout => reading with
            {
                Value = 0,
                Status = "BLACKOUT",
            },

            _ => reading,
        };
    }

    /// <summary>
    ///     Parse a timestamp string and compute elapsed seconds from simulation start.
    ///     Handles multiple ISO 8601 formats: "Z"-suffixed, roundtrip "O" format, and others.
    /// </summary>
    private static bool TryParseTimestamp(string ts, DateTime simulationStart, out double elapsedSeconds)
    {
        elapsedSeconds = 0;
        if (string.IsNullOrWhiteSpace(ts))
            return false;

        // Normalize simulation start to DateTimeOffset so both sides of the subtraction
        // use the same offset. Passing a plain DateTime (UtcKind or LocalKind) creates a
        // DateTimeOffset with the local system offset, matching AddSeconds(DateTimeOffset)
        // behavior in tests.
        var startDto = simulationStart.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(simulationStart, TimeSpan.Zero)
            : new DateTimeOffset(simulationStart, TimeZoneInfo.Local.GetUtcOffset(simulationStart));

        // DateTimeOffset.TryParse handles "Z" suffix, "+HH:mm" offset, and roundtrip "O"
        // format correctly. AdjustToUniversal normalizes any offset to UTC so elapsed is
        // computed as wall-clock seconds from simulation start.
        if (DateTimeOffset.TryParse(ts, null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dto))
        {
            elapsedSeconds = (dto.UtcDateTime - startDto.UtcDateTime).TotalSeconds;
            return true;
        }

        return false;
    }
}
