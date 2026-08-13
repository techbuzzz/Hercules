namespace Hercules.Simulation;

/// <summary>
///     Reads sensor and event fixtures from a template's sim/ directory.
///     All methods throw if fixtures are not found — callers should check
///     <see cref="HasFixtures"/> before replay.
/// </summary>
public interface ISensorSimulator
{
    /// <summary>True if sim/ fixtures exist for the given template.</summary>
    bool HasFixtures(string templateName);

    /// <summary>Load all sensor fixtures from sim/sensors.json.</summary>
    IReadOnlyList<SensorReading> LoadSensorFixtures(string templateName);

    /// <summary>Load all event fixtures from sim/events.json.</summary>
    IReadOnlyList<EventFixture> LoadEventFixtures(string templateName);

    /// <summary>
    ///     Load all failure scenarios from sim/failures.json.
    ///     Returns empty list if the file is absent.
    /// </summary>
    IReadOnlyList<FailureScenario> LoadFailureScenarios(string templateName);

    /// <summary>Get the path to the sim/ directory for a template.</summary>
    string GetSimPath(string templateName);
}
