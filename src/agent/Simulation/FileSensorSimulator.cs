using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Simulation;

/// <summary>
///     File-based sensor/event fixture loader.
///     Reads from <c>templates/{scenario}/sim/</c> source directory.
/// </summary>
public sealed class FileSensorSimulator(
    ILogger<FileSensorSimulator> logger) : ISensorSimulator
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    ///     Base directory for template source files (templates/ in repo root).
    ///     Override for testing.
    /// </summary>
    public string TemplatesBaseDir { get; init; } = "templates";

    public bool HasFixtures(string templateName)
    {
        var path = GetSimPath(templateName);
        return Directory.Exists(path);
    }

    public string GetSimPath(string templateName)
    {
        return Path.Combine(TemplatesBaseDir, templateName, "sim");
    }

    public IReadOnlyList<SensorReading> LoadSensorFixtures(string templateName)
    {
        var path = Path.Combine(GetSimPath(templateName), "sensors.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Sensor fixtures not found at {Path}", path);
            return Array.Empty<SensorReading>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<SensorReading>>(json, JsonOpts);
            logger.LogDebug("Loaded {Count} sensor readings from {Path}",
                items?.Count ?? 0, path);
            return items ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load sensor fixtures from {Path}", path);
            return [];
        }
    }

    public IReadOnlyList<EventFixture> LoadEventFixtures(string templateName)
    {
        var path = Path.Combine(GetSimPath(templateName), "events.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Event fixtures not found at {Path}", path);
            return Array.Empty<EventFixture>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<EventFixture>>(json, JsonOpts);
            logger.LogDebug("Loaded {Count} event fixtures from {Path}",
                items?.Count ?? 0, path);
            return items ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load event fixtures from {Path}", path);
            return [];
        }
    }

    public IReadOnlyList<FailureScenario> LoadFailureScenarios(string templateName)
    {
        var path = Path.Combine(GetSimPath(templateName), "failures.json");
        if (!File.Exists(path))
        {
            logger.LogDebug("No failure scenarios for template {Template}", templateName);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<FailureScenario>>(json, JsonOpts);
            logger.LogInformation("Loaded {Count} failure scenarios for {Template}",
                items?.Count ?? 0, templateName);
            return items ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load failure scenarios from {Path}", path);
            return [];
        }
    }
}
