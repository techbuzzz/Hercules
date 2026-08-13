using Hercules.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI контроллер симуляции шаблонов (task_031).
///     Позволяет запускать симуляцию sensor/event fixtures с failure injection
///     без реального железа или внешних side-effects.
/// </summary>
[ApiController]
[Route("api/simulation")]
public sealed class SimulationController : ControllerBase
{
    private readonly TemplateSimulationService _simulation;

    public SimulationController(TemplateSimulationService simulation)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    /// <summary>Список шаблонов с симуляционными fixtures.</summary>
    [HttpGet("templates")]
    public ActionResult<List<SimulatableTemplateDto>> ListTemplates()
    {
        var templates = _simulation.GetSimulatableTemplates();
        return Ok(templates.Select(t => new SimulatableTemplateDto(t)).ToList());
    }

    /// <summary>Проверить наличие fixtures для шаблона.</summary>
    [HttpGet("templates/{templateName}/available")]
    public ActionResult<TemplateAvailabilityDto> CheckAvailability(string templateName)
    {
        var has = _simulation.HasFixtures(templateName);
        var coverage = has ? _simulation.GetFailureCoverage(templateName) : null;
        return Ok(new TemplateAvailabilityDto(templateName, has, coverage));
    }

    /// <summary>Начать симуляционную сессию.</summary>
    [HttpPost("sessions")]
    public ActionResult<SimulationSessionDto> StartSession([FromBody] StartSessionRequest body)
    {
        if (!_simulation.HasFixtures(body.TemplateName))
        {
            return NotFound($"No simulation fixtures found for template '{body.TemplateName}'.");
        }

        var session = _simulation.StartSession(body.TemplateName);
        return Ok(ToSessionDto(session));
    }

    /// <summary>Запустить replay: получить readings, events и applied failures.</summary>
    [HttpPost("sessions/{templateName}/replay")]
    public ActionResult<SimulationResultDto> Replay(string templateName, [FromBody] ReplayRequest body)
    {
        try
        {
            var result = _simulation.RunReplay(templateName, body.WithFailures);
            return Ok(ToResultDto(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Остановить симуляционную сессию.</summary>
    [HttpDelete("sessions/{templateName}")]
    public ActionResult StopSession(string templateName)
    {
        _simulation.StopSession(templateName);
        return NoContent();
    }

    /// <summary>Получить текущее состояние сессии.</summary>
    [HttpGet("sessions/{templateName}")]
    public ActionResult<SimulationSessionDto> GetSession(string templateName)
    {
        var session = _simulation.GetSession(templateName);
        if (session is null)
            return NotFound($"No active session for '{templateName}'.");
        return Ok(ToSessionDto(session));
    }

    private static SimulationSessionDto ToSessionDto(SimulationSession session)
        => new(
            session.TemplateName,
            session.Scenario,
            session.SensorFixtures.Count,
            session.EventFixtures.Count,
            session.FailureScenarios.Count,
            session.AppliedFailures.Count,
            session.IsRunning,
            session.StartedAt);

    private static SimulationResultDto ToResultDto(SimulationResult result)
        => new(
            result.TemplateName,
            result.Scenario,
            result.Readings.Count,
            result.Events.Count,
            result.AppliedFailures.Select(f => new AppliedFailureDto(
                f.FailureId, f.FailureName, f.Type.ToString(),
                f.InjectedAt, f.ResolvedAt, f.Resolved)).ToList(),
            new SimulationMetricsDto(
                result.Metrics.TotalReadings,
                result.Metrics.TotalEvents,
                result.Metrics.FailuresInjected,
                result.Metrics.FailuresResolved,
                result.Metrics.ElapsedSeconds,
                result.Metrics.StartedAt,
                result.Metrics.CompletedAt));
}

public sealed record SimulatableTemplateDto(string TemplateName);

public sealed record TemplateAvailabilityDto(
    string TemplateName,
    bool HasFixtures,
    Dictionary<string, List<string>>? FailureCoverage);

public sealed record StartSessionRequest(string TemplateName);

public sealed record ReplayRequest(bool WithFailures = true);

public sealed record SimulationSessionDto(
    string TemplateName,
    string Scenario,
    int SensorFixtureCount,
    int EventFixtureCount,
    int FailureScenarioCount,
    int AppliedFailureCount,
    bool IsRunning,
    DateTime? StartedAt);

public sealed record SimulationResultDto(
    string TemplateName,
    string Scenario,
    int ReadingCount,
    int EventCount,
    List<AppliedFailureDto> AppliedFailures,
    SimulationMetricsDto Metrics);

public sealed record AppliedFailureDto(
    string FailureId,
    string FailureName,
    string Type,
    DateTime InjectedAt,
    DateTime ResolvedAt,
    bool Resolved);

public sealed record SimulationMetricsDto(
    int TotalReadings,
    int TotalEvents,
    int FailuresInjected,
    int FailuresResolved,
    double ElapsedSeconds,
    DateTime StartedAt,
    DateTime CompletedAt);
