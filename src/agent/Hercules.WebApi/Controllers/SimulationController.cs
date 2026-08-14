using Hercules.Simulation;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI endpoints симуляции шаблонов (task_031).
///     Позволяют запускать симуляцию sensor/event fixtures с failure injection
///     без реального железа или внешних side-effects.
/// </summary>
public static class SimulationController
{
    public static void MapSimulation(this IEndpointRouteBuilder app)
    {
        // GET /api/simulation/templates — список шаблонов с симуляционными fixtures.
        app.MapGet("/api/simulation/templates", (TemplateSimulationService simulation) =>
        {
            var templates = simulation.GetSimulatableTemplates();
            return Results.Ok(templates.Select(t => new SimulatableTemplateDto(t)).ToList());
        }).WithName("ListSimulationTemplates");

        // GET /api/simulation/templates/{templateName}/available — наличие fixtures для шаблона.
        app.MapGet("/api/simulation/templates/{templateName}/available", (string templateName, TemplateSimulationService simulation) =>
        {
            var has = simulation.HasFixtures(templateName);
            var coverage = has ? simulation.GetFailureCoverage(templateName) : null;
            return Results.Ok(new TemplateAvailabilityDto(templateName, has, coverage));
        }).WithName("SimulationAvailability");

        // POST /api/simulation/sessions — начать симуляционную сессию.
        app.MapPost("/api/simulation/sessions", (StartSessionRequest body, TemplateSimulationService simulation) =>
        {
            if (!simulation.HasFixtures(body.TemplateName))
            {
                return Results.NotFound($"No simulation fixtures found for template '{body.TemplateName}'.");
            }

            var session = simulation.StartSession(body.TemplateName);
            return Results.Ok(ToSessionDto(session));
        }).WithName("StartSimulationSession");

        // POST /api/simulation/sessions/{templateName}/replay — запустить replay.
        app.MapPost("/api/simulation/sessions/{templateName}/replay", (string templateName, ReplayRequest body, TemplateSimulationService simulation) =>
        {
            try
            {
                var result = simulation.RunReplay(templateName, body.WithFailures);
                return Results.Ok(ToResultDto(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).WithName("SimulationReplay");

        // DELETE /api/simulation/sessions/{templateName} — остановить симуляционную сессию.
        app.MapDelete("/api/simulation/sessions/{templateName}", (string templateName, TemplateSimulationService simulation) =>
        {
            simulation.StopSession(templateName);
            return Results.NoContent();
        }).WithName("StopSimulationSession");

        // GET /api/simulation/sessions/{templateName} — текущее состояние сессии.
        app.MapGet("/api/simulation/sessions/{templateName}", (string templateName, TemplateSimulationService simulation) =>
        {
            var session = simulation.GetSession(templateName);
            if (session is null)
            {
                return Results.NotFound($"No active session for '{templateName}'.");
            }

            return Results.Ok(ToSessionDto(session));
        }).WithName("GetSimulationSession");
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
