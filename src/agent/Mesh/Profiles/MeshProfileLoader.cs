using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Profiles;

/// <summary>
///     Loads and validates mesh deployment profiles.
///     Reads from config section and resolves the active profile.
///     Spec: task_070.
/// </summary>
public sealed class MeshProfileLoader
{
    private readonly MeshProfilesConfig _config;
    private readonly ILogger<MeshProfileLoader> _log;
    private readonly Dictionary<string, MeshProfileDefinition> _profiles;
    private MeshProfileDefinition? _activeProfile;

    public MeshProfileLoader(MeshProfilesConfig config, ILogger<MeshProfileLoader> log)
    {
        _config = config;
        _log = log;
        _profiles = new Dictionary<string, MeshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

        // Register built-in Local profile (always available)
        RegisterBuiltInLocalProfile();

        // Load profiles from config section
        LoadFromConfig();

        // Load profiles from files if directory is set
        if (!string.IsNullOrEmpty(_config.ProfilesDir))
        {
            LoadFromFiles();
        }
    }

    /// <summary>
    ///     Returns all registered profile names.
    /// </summary>
    public IReadOnlyList<string> ListProfileNames() => _profiles.Keys.ToList().AsReadOnly();

    /// <summary>
    ///     Returns a profile definition by name, or null if not found.
    /// </summary>
    public MeshProfileDefinition? GetProfile(string name)
    {
        return _profiles.TryGetValue(name, out var profile) ? profile : null;
    }

    /// <summary>
    ///     Returns the active profile. Falls back to "local" if not set or not found.
    /// </summary>
    public MeshProfileDefinition GetActiveProfile()
    {
        if (_activeProfile != null)
            return _activeProfile;

        var activeName = _config.ActiveProfile;
        if (string.IsNullOrEmpty(activeName))
        {
            _log.LogWarning("[MeshProfile] No active profile configured — defaulting to 'local'");
            activeName = "local";
        }

        if (!_profiles.TryGetValue(activeName, out var profile))
        {
            _log.LogWarning("[MeshProfile] Active profile '{Profile}' not found — defaulting to 'local'", activeName);
            profile = _profiles.GetValueOrDefault("local") ?? throw new InvalidOperationException("Built-in 'local' profile not found");
        }
        else
        {
            _log.LogInformation("[MeshProfile] Activated profile: {Profile}", profile.Name);
        }

        _activeProfile = profile;
        return profile;
    }

    /// <summary>
    ///     Returns the effective backend configuration for a given role,
    ///     honouring Enabled flag and fallback to in-process.
    /// </summary>
    public MeshBackendConfig GetEffectiveBackend(string profileName, string backendRole)
    {
        var profile = GetProfile(profileName);
        if (profile == null)
        {
            _log.LogWarning("[MeshProfile] Profile '{Profile}' not found — using local default for {Role}", profileName, backendRole);
            return new MeshBackendConfig { Kind = "in-process", Enabled = false };
        }

        if (!profile.Backends.TryGetValue(backendRole, out var backend))
        {
            _log.LogDebug("[MeshProfile] No backend config for '{Role}' in profile '{Profile}' — using local default", backendRole, profileName);
            return new MeshBackendConfig { Kind = "in-process", Enabled = false };
        }

        if (!backend.Enabled)
        {
            _log.LogDebug("[MeshProfile] Backend '{Role}' disabled in profile '{Profile}' — using local default", backendRole, profileName);
            return new MeshBackendConfig { Kind = "in-process", Enabled = false };
        }

        return backend;
    }

    private void RegisterBuiltInLocalProfile()
    {
        _profiles["local"] = new MeshProfileDefinition
        {
            Name = "local",
            Description = "Single-process, no external dependencies. Uses in-process Channel/ConcurrentDictionary implementations.",
            Profile = MeshBackendProfile.Local,
            Backends = new Dictionary<string, MeshBackendConfig>
            {
                ["bus"] = new() { Kind = "in-process", Enabled = true },
                ["queue"] = new() { Kind = "in-process", Enabled = true },
                ["stateStore"] = new() { Kind = "in-process", Enabled = true },
            },
            Constraints = new MeshProfileConstraints { MaxAgents = 1 },
            DegradationPolicy = new DegradationPolicy { Mode = DegradationMode.FailSilent }
        };
    }

    private void LoadFromConfig()
    {
        if (_config.Profiles == null || _config.Profiles.Count == 0)
        {
            _log.LogDebug("[MeshProfile] No profiles in config — using built-in 'local' only");
            return;
        }

        foreach (var kvp in _config.Profiles)
        {
            try
            {
                var profile = kvp.Value;
                if (string.IsNullOrEmpty(profile.Name))
                    profile.Name = kvp.Key;

                _profiles[kvp.Key] = profile;
                _log.LogDebug("[MeshProfile] Loaded profile from config: {Profile}", kvp.Key);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[MeshProfile] Failed to parse profile '{Key}' from config", kvp.Key);
            }
        }
    }

    private void LoadFromFiles()
    {
        var dir = _config.ProfilesDir;
        if (!Path.IsPathRooted(dir))
            dir = Path.Combine(AppContext.BaseDirectory, dir);

        if (!Directory.Exists(dir))
        {
            _log.LogDebug("[MeshProfile] Profiles directory not found: {Dir}", dir);
            return;
        }

        var files = Directory.GetFiles(dir, "*.meshprofile.json");
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<MeshProfileDefinition>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (profile == null || string.IsNullOrEmpty(profile.Name))
                {
                    _log.LogWarning("[MeshProfile] Profile file {File} has no valid Name — skipping", file);
                    continue;
                }

                var key = Path.GetFileNameWithoutExtension(file);
                _profiles[key] = profile;
                _log.LogDebug("[MeshProfile] Loaded profile from file: {File}", file);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[MeshProfile] Failed to load profile from {File}", file);
            }
        }
    }
}
