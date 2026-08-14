using Hercules.Fleet;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     WebAPI controller for fleet templates (task_062).
///     Lists, shows info, and applies fleet-level bundles: agent template + monitoring + policy + offline defaults.
/// </summary>
[ApiController]
[Route("api/fleet-templates")]
public sealed class FleetTemplateController : ControllerBase
{
    private readonly IFleetTemplateManager _manager;

    public FleetTemplateController(IFleetTemplateManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>List all available fleet templates.</summary>
    [HttpGet]
    public ActionResult<List<FleetTemplateEntryDto>> List()
    {
        var entries = _manager.List();
        return Ok(entries.Select(e => new FleetTemplateEntryDto(
            e.FileName,
            e.Name,
            e.Description,
            e.Vertical,
            e.Version,
            e.FilePath)).ToList());
    }

    /// <summary>Get fleet template manifest.</summary>
    [HttpGet("{fileName}")]
    public ActionResult<FleetTemplateManifestDto> Get(string fileName)
    {
        try
        {
            var manifest = _manager.GetManifest(fileName);
            return Ok(new FleetTemplateManifestDto(
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
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Apply a fleet template: extract agent template and write fleet config files.</summary>
    [HttpPost("{fileName}/apply")]
    public ActionResult<ApplyFleetTemplateResultDto> Apply(string fileName, [FromBody] ApplyFleetTemplateRequest? body)
    {
        var conflict = body?.ConflictResolution ?? FleetConflictResolution.Rename;
        try
        {
            var result = _manager.Apply(fileName, conflict);
            return Ok(new ApplyFleetTemplateResultDto(
                result.TemplateName,
                result.Vertical,
                result.AgentTemplateApplied,
                result.InstalledConfigFiles,
                result.Errors,
                result.HasErrors));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
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
