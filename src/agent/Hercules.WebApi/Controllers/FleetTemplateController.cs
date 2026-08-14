using Hercules.Fleet;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Minimal API endpoints for fleet templates (task_062).
///     Lists, shows info, and applies fleet-level bundles: agent template + monitoring + policy + offline defaults.
/// </summary>
public static class FleetTemplateController
{
    public static void MapFleetTemplates(this IEndpointRouteBuilder app)
    {
        // GET /api/fleet-templates — список всех доступных fleet templates.
        app.MapGet("/api/fleet-templates", (IFleetTemplateManager manager) =>
        {
            var entries = manager.List();
            return Results.Ok(entries.Select(e => new FleetTemplateEntryDto(
                e.FileName,
                e.Name,
                e.Description,
                e.Vertical,
                e.Version,
                e.FilePath)).ToList());
        }).WithName("ListFleetTemplates");

        // GET /api/fleet-templates/{fileName} — манифест fleet template.
        app.MapGet("/api/fleet-templates/{fileName}", (string fileName, IFleetTemplateManager manager) =>
        {
            try
            {
                var manifest = manager.GetManifest(fileName);
                return Results.Ok(new FleetTemplateManifestDto(
                    manifest.Name,
                    manifest.Description,
                    manifest.Version,
                    manifest.Vertical,
                    manifest.AgentTemplateFile,
                    manifest.HardwareBom,
                    manifest.Monitoring,
                    manifest.Policy,
                    manifest.Offline));
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).WithName("GetFleetTemplate");

        // POST /api/fleet-templates/{fileName}/apply — применить fleet template:
        // extract agent template и write fleet config files.
        app.MapPost("/api/fleet-templates/{fileName}/apply", (string fileName, ApplyFleetTemplateRequest? body, IFleetTemplateManager manager) =>
        {
            var conflict = body?.ConflictResolution ?? FleetConflictResolution.Rename;
            try
            {
                var result = manager.Apply(fileName, conflict);
                return Results.Ok(new ApplyFleetTemplateResultDto(
                    result.TemplateName,
                    result.Vertical,
                    result.AgentTemplateApplied,
                    result.InstalledConfigFiles,
                    result.Errors,
                    result.HasErrors));
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).WithName("ApplyFleetTemplate");
    }
}

public sealed record FleetTemplateEntryDto(
    string FileName,
    string Name,
    string Description,
    string Vertical,
    int Version,
    string FilePath);

public sealed record FleetTemplateManifestDto(
    string Name,
    string Description,
    int Version,
    string Vertical,
    string AgentTemplateFile,
    HardwareBom HardwareBom,
    MonitoringConfig Monitoring,
    FleetPolicy Policy,
    OfflineDefaults Offline);

public sealed record ApplyFleetTemplateRequest(FleetConflictResolution ConflictResolution = FleetConflictResolution.Rename);

public sealed record ApplyFleetTemplateResultDto(
    string TemplateName,
    string Vertical,
    bool AgentTemplateApplied,
    List<string> InstalledConfigFiles,
    List<string> Errors,
    bool HasErrors);
