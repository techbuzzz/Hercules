namespace Hercules.Fleet;

/// <summary>
///     Manages fleet template discovery, manifest reading, and application.
///     A fleet template bundles an agent template with fleet-level configuration:
///     hardware BOM, monitoring thresholds, policy constraints, and offline defaults.
/// </summary>
public interface IFleetTemplateManager
{
    /// <summary>Returns the fleet templates directory path.</summary>
    string GetFleetTemplatesDir();

    /// <summary>Lists all available fleet templates.</summary>
    IReadOnlyList<FleetTemplateEntry> List();

    /// <summary>
    ///     Reads and returns the fleet manifest for the given file name.
    ///     Throws if the file is not found or the manifest is invalid.
    /// </summary>
    FleetManifest GetManifest(string fileName);

    /// <summary>
    ///     Applies a fleet template: extracts the agent template, copies fleet config files.
    /// </summary>
    /// <param name="fileName">File name of the .fleettemplate archive.</param>
    /// <param name="conflict">Conflict resolution strategy.</param>
    ApplyFleetTemplateResult Apply(string fileName, FleetConflictResolution conflict = FleetConflictResolution.Rename);
}
