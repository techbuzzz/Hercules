using System.IO.Compression;
using System.Text.Json;
using Hercules.Config;
using Hercules.Skills;

namespace Hercules.Fleet;

/// <summary>
///     Fleet template manager implementation (task_062).
///     Manages fleet templates: ZIP bundles containing an agent template + fleet-level
///     config (hardware BOM, monitoring, policy, offline defaults) for a vertical domain.
///
///     Format — ZIP-archive <c>*.fleettemplate</c> with structure:
///     <list type="bullet">
///         <item>fleet-manifest.json — FleetManifest (required)</item>
///         <item>agent-template/*.agenttemplate — agent template ZIP (optional)</item>
///         <item>monitoring.json — MonitoringConfig (optional)</item>
///         <item>policy.json — FleetPolicy (optional)</item>
///         <item>offline.json — OfflineDefaults (optional)</item>
///         <item>hardware-bom.json — HardwareBom (optional)</item>
///     </list>
/// </summary>
public sealed class FleetTemplateManager : IFleetTemplateManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AgentTemplateManager _agentTemplateManager;
    private readonly StorageConfig _storageConfig;

    public FleetTemplateManager(StorageConfig storageConfig, AgentTemplateManager agentTemplateManager)
    {
        _storageConfig = storageConfig ?? throw new ArgumentNullException(nameof(storageConfig));
        _agentTemplateManager = agentTemplateManager ?? throw new ArgumentNullException(nameof(agentTemplateManager));

        var fleetTemplatesSubdir = storageConfig.Phase2?.FleetTemplatesDir ?? Hercules.BuiltIn.FleetTemplatesSubdir;
        DirectoryPath = Path.Combine(storageConfig.DataRoot, fleetTemplatesSubdir);
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>Fleet templates directory (data/FleetTemplates/).</summary>
    public string DirectoryPath { get; }

    public string GetFleetTemplatesDir() => DirectoryPath;

    public IReadOnlyList<FleetTemplateEntry> List()
    {
        var entries = new List<FleetTemplateEntry>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.fleettemplate"))
        {
            try
            {
                var manifest = ReadManifest(file);
                entries.Add(new FleetTemplateEntry(
                    Path.GetFileName(file),
                    manifest.Name,
                    manifest.Description,
                    manifest.Vertical,
                    manifest.Version,
                    file));
            }
            catch
            {
                // Skip invalid archives
            }
        }
        return entries;
    }

    public FleetManifest GetManifest(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fleet template '{fileName}' not found.");
        return ReadManifest(path);
    }

    public ApplyFleetTemplateResult Apply(string fileName, FleetConflictResolution conflict = FleetConflictResolution.Rename)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return new ApplyFleetTemplateResult
            {
                TemplateName = fileName,
                Errors = new List<string> { $"Fleet template '{fileName}' not found." }
            };
        }

        FleetManifest manifest;
        try
        {
            manifest = ReadManifest(path);
        }
        catch (Exception ex)
        {
            return new ApplyFleetTemplateResult
            {
                TemplateName = fileName,
                Errors = new List<string> { $"Failed to read manifest: {ex.Message}" }
            };
        }

        var result = new ApplyFleetTemplateResult
        {
            TemplateName = manifest.Name,
            Vertical = manifest.Vertical
        };

        using FileStream archiveStream = File.OpenRead(path);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        // 1. Extract agent template (if present)
        var agentEntry = archive.GetEntry("agent-template/" + manifest.AgentTemplateFile);
        if (agentEntry != null)
        {
            try
            {
                var agentTemplateDest = Path.Combine(_agentTemplateManager.DirectoryPath, manifest.AgentTemplateFile);
                if (File.Exists(agentTemplateDest) && conflict == FleetConflictResolution.Skip)
                {
                    result.AgentTemplateApplied = false;
                }
                else
                {
                    if (File.Exists(agentTemplateDest))
                    {
                        if (conflict == FleetConflictResolution.Fail)
                        {
                            result.Errors.Add($"Agent template '{manifest.AgentTemplateFile}' already exists. Use Rename to back it up.");
                            return result;
                        }
                        // Rename: move existing to *.bak
                        File.Move(agentTemplateDest, agentTemplateDest + ".bak", overwrite: true);
                    }
                    ExtractEntry(agentEntry, agentTemplateDest);
                    result.AgentTemplateApplied = true;
                    result.InstalledConfigFiles.Add("agent-template/" + manifest.AgentTemplateFile);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to extract agent template: {ex.Message}");
            }
        }
        else if (!string.IsNullOrEmpty(manifest.AgentTemplateFile))
        {
            // Agent template referenced but not bundled — try to apply from the existing templates directory
            try
            {
                var existingPath = Path.Combine(_agentTemplateManager.DirectoryPath, manifest.AgentTemplateFile);
                if (File.Exists(existingPath))
                {
                    // Agent template already present — consider it applied
                    result.AgentTemplateApplied = true;
                    result.InstalledConfigFiles.Add("agent-template/" + manifest.AgentTemplateFile + " (pre-installed)");
                }
                else
                {
                    result.Errors.Add($"Agent template '{manifest.AgentTemplateFile}' not found in fleet template archive and not present in data directory.");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to check agent template: {ex.Message}");
            }
        }

        // 2. Extract fleet config files to data root
        var fleetConfigFiles = new[]
        {
            ("monitoring.json", typeof(MonitoringConfig)),
            ("policy.json", typeof(FleetPolicy)),
            ("offline.json", typeof(OfflineDefaults)),
            ("hardware-bom.json", typeof(HardwareBom)),
        };

        foreach (var (cfgFileName, _) in fleetConfigFiles)
        {
            var entry = archive.GetEntry(cfgFileName);
            if (entry == null) continue;

            var destPath = Path.Combine(_storageConfig.DataRoot, cfgFileName);
            if (File.Exists(destPath))
            {
                if (conflict == FleetConflictResolution.Skip)
                    continue;
                if (conflict == FleetConflictResolution.Fail)
                {
                    result.Errors.Add($"Config file '{cfgFileName}' already exists.");
                    continue;
                }
                File.Move(destPath, destPath + ".bak", overwrite: true);
            }

            try
            {
                ExtractEntry(entry, destPath);
                result.InstalledConfigFiles.Add(cfgFileName);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to extract '{cfgFileName}': {ex.Message}");
            }
        }

        return result;
    }

    private static FleetManifest ReadManifest(string zipPath)
    {
        using FileStream stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("fleet-manifest.json")
            ?? throw new InvalidOperationException("fleet-manifest.json not found in fleet template archive.");
        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<FleetManifest>(json, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize fleet manifest.");
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destPath)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using Stream entryStream = entry.Open();
        using FileStream fileStream = File.Create(destPath);
        entryStream.CopyTo(fileStream);
    }
}
