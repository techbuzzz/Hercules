using Hercules.Skills;
using Microsoft.Extensions.Logging;

namespace Hercules.Simulation;

/// <summary>
///     Orchestrates template simulation: loads fixtures, injects failures,
///     replays sensor/event streams without real hardware or external side-effects.
/// </summary>
public sealed class TemplateSimulationService(
    ISensorSimulator simulator,
    FailureScenarioEngine failureEngine,
    AgentTemplateManager templateManager,
    ILogger<TemplateSimulationService> logger)
{
    /// <summary>
    ///     All available simulation sessions (keyed by template name).
    /// </summary>
    private readonly Dictionary<string, SimulationSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     List all templates that have simulation fixtures.
    /// </summary>
    public IReadOnlyList<string> GetSimulatableTemplates()
    {
        var available = templateManager.List();
        return available
            .Where(t => simulator.HasFixtures(Path.GetFileNameWithoutExtension(t.FileName)))
            .Select(t => Path.GetFileNameWithoutExtension(t.FileName))
            .ToList();
    }

    /// <summary>
    ///     Check if a template has simulation fixtures.
    /// </summary>
    public bool HasFixtures(string templateName)
        => simulator.HasFixtures(templateName);

    /// <summary>
    ///     Start a new simulation session for a template.
    /// </summary>
    public SimulationSession StartSession(string templateName, DateTime? sessionStartTime = null)
    {
        if (_sessions.TryGetValue(templateName, out var existing) && existing.IsRunning)
        {
            logger.LogWarning("Session for template '{Template}' is already running", templateName);
            return existing;
        }

        var scenario = Path.GetFileNameWithoutExtension(templateName);
        var session = new SimulationSession
        {
            TemplateName = templateName,
            Scenario = scenario,
            SensorFixtures = [.. simulator.LoadSensorFixtures(scenario)],
            EventFixtures = [.. simulator.LoadEventFixtures(scenario)],
            FailureScenarios = [.. simulator.LoadFailureScenarios(scenario)],
            IsRunning = true,
            StartedAt = sessionStartTime ?? DateTime.UtcNow,
        };

        _sessions[templateName] = session;

        logger.LogInformation(
            "Started simulation session for '{Template}': {Sensors} sensors, {Events} events, {Failures} failure scenarios",
            templateName,
            session.SensorFixtures.Count,
            session.EventFixtures.Count,
            session.FailureScenarios.Count);

        return session;
    }

    /// <summary>
    ///     Run replay: returns readings and events for the current session.
    ///     If <paramref name="withFailures"/> is true, failure injection is applied.
    /// </summary>
    public SimulationResult RunReplay(string templateName, bool withFailures = true)
    {
        if (!_sessions.TryGetValue(templateName, out var session) || !session.IsRunning)
        {
            throw new InvalidOperationException($"No active session for template '{templateName}'. Call StartSession first.");
        }

        var startedAt = DateTime.UtcNow;

        // Sensor readings
        var readings = withFailures && session.FailureScenarios.Count > 0
            ? failureEngine.InjectFailures(
                session.SensorFixtures,
                session.FailureScenarios,
                session.StartedAt ?? startedAt)
            : [.. session.SensorFixtures];

        // Generate failure events if injecting
        var events = new List<EventFixture>(session.EventFixtures);
        if (withFailures && session.FailureScenarios.Count > 0)
        {
            var failureEvents = failureEngine.GenerateFailureEvents(
                session.FailureScenarios,
                session.StartedAt ?? startedAt);
            events.AddRange(failureEvents);
        }

        // Track applied failures
        session.AppliedFailures.Clear();
        foreach (var scenario in session.FailureScenarios)
        {
            session.AppliedFailures.Add(new AppliedFailure(
                FailureId: scenario.Id,
                FailureName: scenario.Name,
                Type: scenario.Type,
                InjectedAt: (session.StartedAt ?? startedAt).AddSeconds(scenario.InjectAfterSeconds),
                ResolvedAt: (session.StartedAt ?? startedAt)
                    .AddSeconds(scenario.InjectAfterSeconds + scenario.DurationSeconds),
                Resolved: true));
        }

        session.GeneratedReadings.Clear();
        session.GeneratedReadings.AddRange(readings);

        session.GeneratedEvents.Clear();
        session.GeneratedEvents.AddRange(events);

        var completedAt = DateTime.UtcNow;

        logger.LogInformation(
            "Completed replay for '{Template}': {Readings} readings, {Events} events, {Failures} failures applied",
            templateName, readings.Count, events.Count, session.FailureScenarios.Count);

        return new SimulationResult(
            TemplateName: templateName,
            Scenario: session.Scenario,
            Readings: readings,
            Events: events,
            AppliedFailures: session.AppliedFailures,
            Metrics: new SimulationMetrics(
                TotalReadings: readings.Count,
                TotalEvents: events.Count,
                FailuresInjected: session.FailureScenarios.Count,
                FailuresResolved: session.AppliedFailures.Count(a => a.Resolved),
                ElapsedSeconds: (completedAt - startedAt).TotalSeconds,
                StartedAt: startedAt,
                CompletedAt: completedAt));
    }

    /// <summary>
    ///     Stop an active simulation session.
    /// </summary>
    public void StopSession(string templateName)
    {
        if (_sessions.TryGetValue(templateName, out var session))
        {
            session.IsRunning = false;
            logger.LogInformation("Stopped simulation session for '{Template}'", templateName);
        }
    }

    /// <summary>
    ///     Get current session state.
    /// </summary>
    public SimulationSession? GetSession(string templateName)
        => _sessions.GetValueOrDefault(templateName);

    /// <summary>
    ///     Get failure coverage for a template without starting a session.
    /// </summary>
    public Dictionary<string, List<string>> GetFailureCoverage(string templateName)
    {
        var scenarios = simulator.LoadFailureScenarios(templateName);
        return failureEngine.GetFailureCoverage(scenarios);
    }
}
