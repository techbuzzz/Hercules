using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Config;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Registry;

/// <summary>
///     Discovers tool declarations from file system (data/Tools/*.tool.json).
///     Allows runtime registration of external/dynamic tools without recompilation.
/// </summary>
public static class ToolDiscovery
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Scan tools directory and register discovered tools with the registry.
    /// </summary>
    /// <param name="config">AppConfig (includes ToolRegistryConfig.ToolsDir).</param>
    /// <param name="registry">Registry service to register discovered tools.</param>
    /// <param name="policyEngine">Policy engine for descriptor inference.</param>
    /// <param name="logger">Logger for discovery output.</param>
    /// <returns>Number of tools discovered from files.</returns>
    public static int Discover(
        AppConfig config,
        IToolRegistryService registry,
        ToolPolicyEngine? policyEngine,
        ILogger? logger = null)
    {
        if (!config.ToolRegistry.Enabled)
        {
            logger?.LogInformation("[ToolDiscovery] Registry disabled — skipping discovery");
            return 0;
        }

        var toolsDir = config.ToolRegistry.ToolsDir;
        if (string.IsNullOrWhiteSpace(toolsDir))
        {
            logger?.LogInformation("[ToolDiscovery] ToolsDir not configured — skipping");
            return 0;
        }

        // Resolve relative to data directory
        var baseDir = config.Storage?.DataRoot ?? ".";
        var fullPath = Path.IsPathRooted(toolsDir)
            ? toolsDir
            : Path.Combine(baseDir, toolsDir);

        if (!Directory.Exists(fullPath))
        {
            logger?.LogInformation("[ToolDiscovery] Tools directory does not exist: {Path}", fullPath);
            return 0;
        }

        var files = Directory.GetFiles(fullPath, "*.tool.json", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            logger?.LogInformation("[ToolDiscovery] No .tool.json files found in {Path}", fullPath);
            return 0;
        }

        int registered = 0;
        foreach (var file in files)
        {
            try
            {
                var entry = LoadToolDeclaration(file, policyEngine);
                if (entry != null)
                {
                    registry.RegisterEntry(entry);
                    logger?.LogInformation("[ToolDiscovery] Loaded tool: {Name} from {File}",
                        entry.Name, Path.GetFileName(file));
                    registered++;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[ToolDiscovery] Failed to load {File}", file);
            }
        }

        logger?.LogInformation("[ToolDiscovery] Discovered {Count}/{Total} tools from files",
            registered, files.Length);

        return registered;
    }

    private static ToolRegistryEntry? LoadToolDeclaration(string file, ToolPolicyEngine? policyEngine)
    {
        var json = File.ReadAllText(file);
        var decl = JsonSerializer.Deserialize<ToolDeclarationFile>(json, JsonOpts);
        if (decl is null || string.IsNullOrWhiteSpace(decl.Name))
            return null;

        // Merge with policy engine descriptor if available
        var descriptor = policyEngine?.GetDescriptor(decl.Name);

        // Override descriptor fields from file if specified
        if (decl.Descriptor != null)
        {
            var d = descriptor ?? new ToolDescriptor { Name = decl.Name };
            descriptor = new ToolDescriptor
            {
                Name = d.Name,
                InputSchema = decl.Descriptor.InputSchema ?? d.InputSchema,
                OutputSchema = d.OutputSchema,
                SideEffectLevel = decl.Descriptor.SideEffectLevel ?? d.SideEffectLevel,
                RequiredPermissions = decl.Descriptor.RequiredPermissions ?? d.RequiredPermissions,
                TimeoutSeconds = decl.Descriptor.TimeoutSeconds is > 0 ? decl.Descriptor.TimeoutSeconds.Value : d.TimeoutSeconds,
                MaxRetries = decl.Descriptor.MaxRetries ?? d.MaxRetries,
                Idempotent = decl.Descriptor.Idempotent ?? d.Idempotent,
            };
        }

        return new ToolRegistryEntry
        {
            Name = decl.Name,
            Category = decl.Category ?? ToolCategory.Unknown,
            Description = decl.Description ?? "",
            Descriptor = descriptor,
            Enabled = decl.Enabled ?? true,
            Limits = decl.Limits,
            RegisteredAt = DateTime.UtcNow,
            Source = "File",
            SupportsHealthCheck = decl.SupportsHealthCheck ?? false,
            HealthState = (decl.Enabled == false)
                ? new ToolHealthState(ToolHealthStatus.Disabled, DateTime.UtcNow, "Disabled in config", 0)
                : ToolHealthState.Initial,
        };
    }
}

/// <summary>
///     Формат .tool.json файла для декларации tool.
/// </summary>
internal sealed class ToolDeclarationFile
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public ToolCategory? Category { get; set; }

    public bool? Enabled { get; set; }

    public bool? SupportsHealthCheck { get; set; }

    public ToolDescriptorOverride? Descriptor { get; set; }

    public ToolLimits? Limits { get; set; }
}

/// <summary>
///     Override для ToolDescriptor из файла.
/// </summary>
internal sealed class ToolDescriptorOverride
{
    public SideEffectLevel? SideEffectLevel { get; set; }
    public ToolPermission? RequiredPermissions { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? MaxRetries { get; set; }
    public bool? Idempotent { get; set; }
    public string? InputSchema { get; set; }
}
