using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hercules.Config.Rollout;

/// <summary>
///     Staged rollout manager: Pending → Staging → Production → Retired (task_058).
///     Поддерживает expiry, last-known-good fallback, и history.
/// </summary>
public sealed class RolloutManager : IRolloutManager
{
    private readonly ConfigRolloutConfig _config;
    private readonly ISignedBundleValidator _validator;
    private readonly LocalConfigValidator _localValidator;
    private readonly RuntimeConfigStore _configStore;
    private readonly ILogger<RolloutManager> _logger;

    private readonly string _bundlesDir;
    private readonly string _stateFilePath;
    private readonly object _lock = new();
    private RolloutState _state;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public RolloutManager(
        ConfigRolloutConfig config,
        ISignedBundleValidator validator,
        LocalConfigValidator localValidator,
        RuntimeConfigStore configStore,
        ILogger<RolloutManager> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _localValidator = localValidator ?? throw new ArgumentNullException(nameof(localValidator));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _bundlesDir = Hercules.BuiltIn.ResolvePathUnderDataRoot(
            Hercules.BuiltIn.ResolveDataRoot(),
            _config.BundlesPath);
        _stateFilePath = Path.Combine(_bundlesDir, Hercules.BuiltIn.RolloutStateFileName);
        Directory.CreateDirectory(_bundlesDir);

        _state = LoadState();
    }

    public RolloutState GetState()
    {
        lock (_lock)
        {
            return CloneState(_state);
        }
    }

    public async Task<RolloutResult> ApplyBundleAsync(ConfigBundle bundle, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        // 1. Signature validation
        var sigResult = await _validator.ValidateAsync(bundle, ct);
        if (!sigResult.IsValid)
        {
            _logger.LogWarning(
                "Bundle {BundleId} v{Version} rejected: signature validation failed: {Errors}",
                bundle.Id, bundle.Version, string.Join("; ", sigResult.Errors));

            return new RolloutResult(false, $"Signature validation failed: {string.Join("; ", sigResult.Errors)}",
                null, BundleStage.Pending, null);
        }

        // 2. Local payload validation
        var localResult = _localValidator.Validate(bundle);
        if (!localResult.IsValid)
        {
            _logger.LogWarning(
                "Bundle {BundleId} v{Version} rejected: local validation failed: {Errors}",
                bundle.Id, bundle.Version, string.Join("; ", localResult.Errors));

            return new RolloutResult(false, $"Local validation failed: {string.Join("; ", localResult.Errors)}",
                null, BundleStage.Pending, null);
        }

        if (localResult.Warnings.Count > 0)
        {
            _logger.LogInformation(
                "Bundle {BundleId} v{Version} applied with warnings: {Warnings}",
                bundle.Id, bundle.Version, string.Join("; ", localResult.Warnings));
        }

        // 3. Save bundle
        await SaveBundleAsync(bundle, ct);

        lock (_lock)
        {
            // Set pending
            _state.PendingBundleId = bundle.Id;
            _state.PendingVersion = bundle.Version;
            bundle.Stage = BundleStage.Pending;
            SaveStateLocked();

            // Auto-apply if autoPromote is enabled
            if (_config.AutoPromoteToStaging)
            {
                bundle.Stage = BundleStage.Staging;
                _state.PendingBundleId = null;
                SaveStateLocked();
                SaveBundleLocked(bundle);
            }

            _logger.LogInformation(
                "Bundle {BundleId} v{Version} applied (stage={Stage})",
                bundle.Id, bundle.Version, bundle.Stage);
        }

        return new RolloutResult(true, null, bundle.Id, bundle.Stage, null);
    }

    public async Task<RolloutResult> PromoteStageAsync(string bundleId, CancellationToken ct = default)
    {
        var bundle = GetBundle(bundleId);
        if (bundle == null)
        {
            return new RolloutResult(false, $"Bundle '{bundleId}' not found.", null, BundleStage.Pending, null);
        }

        BundleStage nextStage;

        lock (_lock)
        {
            nextStage = bundle.Stage switch
            {
                BundleStage.Pending => BundleStage.Staging,
                BundleStage.Staging => BundleStage.Production,
                _ => bundle.Stage
            };

            if (nextStage == bundle.Stage)
            {
                return new RolloutResult(false, $"Bundle '{bundleId}' is already at final stage '{bundle.Stage}'.", bundle.Id, bundle.Stage, null);
            }

            bundle.Stage = nextStage;

            if (nextStage == BundleStage.Production)
            {
                // Save as LKG before applying
                if (!string.IsNullOrEmpty(_state.CurrentBundleId) && _state.CurrentBundleId != bundleId)
                {
                    _state.LastKnownGoodBundleId = _state.CurrentBundleId;
                    _state.LastKnownGoodVersion = _state.CurrentVersion;
                }

                bundle.AppliedAt = DateTime.UtcNow;
                _state.CurrentBundleId = bundle.Id;
                _state.CurrentVersion = bundle.Version;
                _state.CurrentStage = BundleStage.Production;
                _state.LastPromotedAt = DateTime.UtcNow;
                _state.PendingBundleId = null;
                _state.PendingVersion = null;
            }

            AddHistoryLocked(bundle, nextStage == BundleStage.Production ? "promoted" : "promoted_to_staging");
            SaveStateLocked();
            SaveBundleLocked(bundle);
        }

        // Apply config payload to RuntimeConfigStore for production bundles
        if (nextStage == BundleStage.Production)
        {
            try
            {
                var appConfig = JsonSerializer.Deserialize<AppConfig>(bundle.Payload, JsonOptions);
                if (appConfig != null)
                {
                    _configStore.Update(appConfig);
                    _logger.LogInformation(
                        "Config from bundle {BundleId} v{Version} applied to RuntimeConfigStore",
                        bundle.Id, bundle.Version);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply config from bundle {BundleId}", bundle.Id);
                return new RolloutResult(false, $"Failed to apply config: {ex.Message}", bundle.Id, nextStage, null);
            }
        }

        _logger.LogInformation(
            "Bundle {BundleId} v{Version} promoted to stage {Stage}",
            bundle.Id, bundle.Version, nextStage);

        return new RolloutResult(true, null, bundle.Id, nextStage, null);
    }

    public Task<RolloutResult> RollbackAsync(string? reason = null, CancellationToken ct = default)
    {
        string? lkgId;
        string? lkgVersion;

        lock (_lock)
        {
            lkgId = _state.LastKnownGoodBundleId;
            lkgVersion = _state.LastKnownGoodVersion;

            if (string.IsNullOrEmpty(lkgId))
            {
                return Task.FromResult(new RolloutResult(
                    false, "No last-known-good bundle to rollback to.", null, _state.CurrentStage, null));
            }

            var lkgBundle = LoadBundleLocked(lkgId);
            if (lkgBundle == null)
            {
                return Task.FromResult(new RolloutResult(
                    false, $"Last-known-good bundle '{lkgId}' not found on disk.", null, _state.CurrentStage, null));
            }

            // Apply LKG to RuntimeConfigStore
            try
            {
                var appConfig = JsonSerializer.Deserialize<AppConfig>(lkgBundle.Payload, JsonOptions);
                if (appConfig != null)
                {
                    _configStore.Update(appConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback to LKG bundle {BundleId}", lkgId);
                return Task.FromResult(new RolloutResult(false, $"Rollback failed: {ex.Message}", lkgId, BundleStage.Production, null));
            }

            // Update state
            _state.CurrentBundleId = lkgId;
            _state.CurrentVersion = lkgVersion;
            _state.CurrentStage = BundleStage.Production;
            _state.PendingBundleId = null;
            _state.PendingVersion = null;
            _state.LastRollbackAt = DateTime.UtcNow;

            AddHistoryLocked(lkgBundle, "rolled_back", reason ?? "Manual rollback");
            SaveStateLocked();

            _logger.LogInformation(
                "Rolled back to LKG bundle {BundleId} v{Version}. Reason: {Reason}",
                lkgId, lkgVersion, reason ?? "not provided");
        }

        return Task.FromResult(new RolloutResult(true, null, lkgId, BundleStage.Production, "rollback_triggered"));
    }

    public void CheckExpiry()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // Check pending bundle
            if (!string.IsNullOrEmpty(_state.PendingBundleId))
            {
                var pending = LoadBundleLocked(_state.PendingBundleId);
                if (pending != null && pending.ExpiresAt.HasValue && pending.ExpiresAt.Value < now)
                {
                    pending.Stage = BundleStage.Retired;
                    SaveBundleLocked(pending);
                    AddHistoryLocked(pending, "expired");
                    _state.PendingBundleId = null;
                    _state.PendingVersion = null;
                    SaveStateLocked();
                    _logger.LogWarning("Pending bundle {BundleId} expired and retired", pending.Id);
                }
            }

            // Check staging bundle
            var stagingBundles = Directory.GetFiles(_bundlesDir, "*.bundle.json")
                .Select(f => LoadBundleLocked(Path.GetFileNameWithoutExtension(f).Replace(".bundle", "")))
                .Where(b => b != null && b.Stage == BundleStage.Staging)
                .ToList();

            foreach (var bundle in stagingBundles)
            {
                if (bundle == null) continue;

                var stagedAt = bundle.AppliedAt ?? bundle.CreatedAt;
                var expiryWindow = stagedAt.AddMinutes(bundle.StagingDurationMinutes);

                if (now > expiryWindow)
                {
                    bundle.Stage = BundleStage.Retired;
                    SaveBundleLocked(bundle);
                    AddHistoryLocked(bundle, "expired");
                    _logger.LogWarning(
                        "Staging bundle {BundleId} expired after {Minutes} minutes",
                        bundle.Id, bundle.StagingDurationMinutes);
                }
            }

            SaveStateLocked();
        }
    }

    public ConfigBundle? GetBundle(string bundleId)
    {
        lock (_lock)
        {
            return LoadBundleLocked(bundleId);
        }
    }

    // --- Persistence helpers ---

    private async Task SaveBundleAsync(ConfigBundle bundle, CancellationToken ct)
    {
        var path = GetBundlePath(bundle.Id);
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private void SaveBundleLocked(ConfigBundle bundle)
    {
        var path = GetBundlePath(bundle.Id);
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        File.WriteAllText(path, json);
    }

    private ConfigBundle? LoadBundleLocked(string bundleId)
    {
        var path = GetBundlePath(bundleId);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ConfigBundle>(File.ReadAllText(path), JsonOptions)
            : null;
    }

    private string GetBundlePath(string bundleId) =>
        Path.Combine(_bundlesDir, $"{bundleId}.bundle.json");

    private RolloutState LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                return JsonSerializer.Deserialize<RolloutState>(
                    File.ReadAllText(_stateFilePath), JsonOptions) ?? new();
            }
            catch
            {
                return new();
            }
        }
        return new();
    }

    private void SaveStateLocked()
    {
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        File.WriteAllText(_stateFilePath, json);
    }

    private void AddHistoryLocked(ConfigBundle bundle, string action, string? reason = null)
    {
        _state.History.Add(new RolloutHistoryEntry
        {
            BundleId = bundle.Id,
            Version = bundle.Version,
            Action = action,
            Timestamp = DateTime.UtcNow,
            Reason = reason
        });

        // Keep last 100 entries
        if (_state.History.Count > 100)
        {
            _state.History = _state.History.TakeLast(100).ToList();
        }
    }

    private static RolloutState CloneState(RolloutState s) => new()
    {
        CurrentBundleId = s.CurrentBundleId,
        CurrentVersion = s.CurrentVersion,
        CurrentStage = s.CurrentStage,
        LastKnownGoodBundleId = s.LastKnownGoodBundleId,
        LastKnownGoodVersion = s.LastKnownGoodVersion,
        PendingBundleId = s.PendingBundleId,
        PendingVersion = s.PendingVersion,
        LastPromotedAt = s.LastPromotedAt,
        LastRollbackAt = s.LastRollbackAt,
        History = s.History.ToList()
    };
}
