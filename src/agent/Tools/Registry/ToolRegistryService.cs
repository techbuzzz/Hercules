using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Hercules.Config;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools.Registry;

/// <summary>
///     Расширенный реестр инструментов с health tracking, allow/deny и категориями.
///     Композирует базовый <see cref="ToolRegistry"/> и добавляет:
///
///     - per-tool health state
///     - allow/deny pattern filtering
///     - категоризация tools
///     - enable/disable API
///     - file-based discovery
/// </summary>
public sealed class ToolRegistryService : IToolRegistryService
{
    private readonly ConcurrentDictionary<string, ToolRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolRegistryConfig _config;
    private readonly ToolPolicyEngine? _policyEngine;
    private readonly ILogger<ToolRegistryService> _logger;

    public ToolRegistryService(
        IEnumerable<ITool> tools,
        ToolRegistryConfig config,
        ToolPolicyEngine? policyEngine,
        ILogger<ToolRegistryService> logger)
    {
        _config = config;
        _policyEngine = policyEngine;
        _logger = logger;

        foreach (var tool in tools)
        {
            var entry = CreateEntryFromTool(tool);
            _entries[entry.Name] = entry;
        }

        _logger.LogInformation("[ToolRegistry] Initialized with {Count} tools from DI", _entries.Count);
    }

    /// <inheritdoc />
    public ToolRegistryEntry? GetEntry(string name) =>
        _entries.TryGetValue(name, out var e) ? e : null;

    /// <inheritdoc />
    public IReadOnlyCollection<ToolRegistryEntry> GetAllEntries() =>
        _entries.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public IEnumerable<ToolRegistryEntry> GetByCategory(ToolCategory category) =>
        _entries.Values.Where(e => e.Category == category);

    /// <inheritdoc />
    public IEnumerable<string> GetAllowedTools() =>
        _entries.Values
            .Where(e => e.Enabled && IsNameAllowed(e.Name))
            .Select(e => e.Name);

    /// <inheritdoc />
    public bool IsAllowed(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            return false;
        return entry.Enabled && IsNameAllowed(name);
    }

    /// <inheritdoc />
    public void UpdateHealthState(string name, ToolHealthState state)
    {
        if (_entries.TryGetValue(name, out var entry))
        {
            entry.HealthState = state;
            _logger.LogDebug("[ToolRegistry] Health state for '{Name}': {Status}, failures={Failures}",
                name, state.Status, state.ConsecutiveFailures);
        }
    }

    /// <inheritdoc />
    public void SetEnabled(string name, bool enabled)
    {
        if (_entries.TryGetValue(name, out var entry))
        {
            entry.Enabled = enabled;
            entry.HealthState = enabled
                ? new ToolHealthState(ToolHealthStatus.Unknown, DateTime.MinValue, null, 0)
                : new ToolHealthState(ToolHealthStatus.Disabled, DateTime.UtcNow, "Manually disabled", 0);
            _logger.LogInformation("[ToolRegistry] Tool '{Name}' enabled={Enabled}", name, enabled);
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        _logger.LogInformation("[ToolRegistry] Reload triggered — re-scanning tools directory");
        // Re-discovery will be handled by ToolDiscovery calling RegisterEntry
    }

    /// <inheritdoc />
    public void RegisterEntry(ToolRegistryEntry entry)
    {
        if (!_config.Enabled)
        {
            _logger.LogDebug("[ToolRegistry] Registry disabled — skipping entry '{Name}'", entry.Name);
            return;
        }

        if (!IsNameAllowed(entry.Name))
        {
            _logger.LogDebug("[ToolRegistry] Tool '{Name}' denied by patterns — skipping", entry.Name);
            return;
        }

        if (_entries.TryGetValue(entry.Name, out var existing))
        {
            // Merge: preserve health state but update descriptor/limits from file
            existing.Descriptor = entry.Descriptor ?? existing.Descriptor;
            existing.Limits = entry.Limits ?? existing.Limits;
            existing.Category = entry.Category != ToolCategory.Unknown
                ? entry.Category
                : existing.Category;
            _logger.LogDebug("[ToolRegistry] Merged entry for '{Name}' from {Source}", entry.Name, entry.Source);
        }
        else
        {
            _entries[entry.Name] = entry;
            _logger.LogInformation("[ToolRegistry] Registered tool '{Name}' (category={Category}, source={Source})",
                entry.Name, entry.Category, entry.Source);
        }
    }

    private bool IsNameAllowed(string name)
    {
        if (_config.AllowedPatterns.Count == 0 || _config.AllowedPatterns.Contains("*"))
            return !IsDenied(name);

        if (!MatchesAny(name, _config.AllowedPatterns))
            return false;

        return !IsDenied(name);
    }

    private bool IsDenied(string name)
    {
        return MatchesAny(name, _config.DeniedPatterns);
    }

    private static bool MatchesAny(string name, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(name, pattern))
                return true;
        }
        return false;
    }

    /// <summary>
    ///     Простой glob match: *, ?, **/*.
    ///     Converts glob to regex for matching.
    /// </summary>
    private static bool GlobMatch(string name, string pattern)
    {
        if (pattern == "*")
            return true;

        // Build regex from glob
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*\\/", "(.+/)?")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }

    private ToolRegistryEntry CreateEntryFromTool(ITool tool)
    {
        var descriptor = _policyEngine?.GetDescriptor(tool.Name);
        var category = InferCategory(tool.Name, descriptor);

        return new ToolRegistryEntry
        {
            Name = tool.Name,
            Category = category,
            Description = tool.Description,
            Descriptor = descriptor,
            Enabled = true,
            HealthState = new ToolHealthState(ToolHealthStatus.Unknown, DateTime.MinValue, null, 0),
            RegisteredAt = DateTime.UtcNow,
            Source = "Internal",
            SupportsHealthCheck = tool is IHealthCheckableTool,
        };
    }

    private static ToolCategory InferCategory(string name, ToolDescriptor? descriptor)
    {
        var n = name.ToLowerInvariant();

        if (n.Contains("http") || n.Contains("fetch") || n.Contains("request") || n.Contains("web"))
            return ToolCategory.Http;
        if (n.Contains("file") || n.Contains("fs") || n.Contains("read") || n.Contains("write"))
            return ToolCategory.FileSystem;
        if (n.Contains("shell") || n.Contains("exec") || n.Contains("bash") || n.Contains("cmd"))
            return ToolCategory.Shell;
        if (n.Contains("db") || n.Contains("sql") || n.Contains("database") || n.Contains("postgres"))
            return ToolCategory.Database;
        if (n.Contains("gpio") || n.Contains("hardware") || n.Contains("serial"))
            return ToolCategory.Gpio;
        if (n.Contains("mqtt") || n.Contains("iot"))
            return ToolCategory.Mqtt;
        if (n.Contains("mcp") || n.Contains("model_context"))
            return ToolCategory.Mcp;
        if (n.Contains("code") || n.Contains("execute") || n.Contains("wasm") || n.Contains("sandbox"))
            return ToolCategory.CodeExecution;

        if (descriptor != null)
        {
            return descriptor.SideEffectLevel switch
            {
                SideEffectLevel.Critical => ToolCategory.Shell,
                SideEffectLevel.External => ToolCategory.Http,
                SideEffectLevel.Local => ToolCategory.FileSystem,
                _ => ToolCategory.Internal,
            };
        }

        return ToolCategory.Internal;
    }
}

/// <summary>
///     Marker interface для tools, поддерживающих health check.
/// </summary>
public interface IHealthCheckableTool
{
    Task<bool> CheckHealthAsync(CancellationToken ct = default);
}
