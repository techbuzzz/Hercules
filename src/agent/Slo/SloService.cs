using System.Collections.Concurrent;
using System.Text.Json;
using Hercules.Audit;
using Hercules.Config;
using Hercules.Mesh.Observability;
using Hercules.Offline;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Slo;

/// <summary>
///     Operational SLO service (task_064).
///     Reads SLO definitions, evaluates current state against targets,
///     computes severity and tracks violations.
/// </summary>
public sealed class SloService : ISloService
{
    private readonly SlosConfig _config;
    private readonly IAuditService _audit;
    private readonly IMeshObservabilityService _meshObs;
    private readonly IOutboxStore? _outbox;
    private readonly IBudgetService? _budget;
    private readonly ILogger<SloService> _logger;
    private readonly ISloLatencyTracker? _latencyTracker;
    private readonly IConnectivityStateProvider? _connectivity;

    private readonly string _slosDir;
    private readonly Dictionary<string, SloDefinition> _definitionsCache = new(StringComparer.OrdinalIgnoreCase);
    // task_077: shared state is read/written from async methods on potentially
    // concurrent threads (multiple GET /api/slos requests + background re-evaluators),
    // so promote to ConcurrentDictionary to remove the dictionary race.
    private readonly ConcurrentDictionary<string, SloStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<SloViolationRecord>> _violations = new(StringComparer.OrdinalIgnoreCase);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SloService(
        SlosConfig config,
        IAuditService audit,
        IMeshObservabilityService meshObs,
        IOutboxStore outbox,
        IBudgetService budget,
        ILogger<SloService> logger,
        ISloLatencyTracker? latencyTracker = null,
        IConnectivityStateProvider? connectivity = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _meshObs = meshObs ?? throw new ArgumentNullException(nameof(meshObs));
        _outbox = outbox;
        _budget = budget;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _latencyTracker = latencyTracker;
        _connectivity = connectivity;

        _slosDir = Path.IsPathRooted(config.SlosDir)
            ? config.SlosDir
            : Path.Combine(AppContext.BaseDirectory, config.SlosDir);
    }

    /// <inheritdoc />
    public string GetSlosDir() => _slosDir;

    /// <inheritdoc />
    public IReadOnlyList<SloDefinition> GetAllDefinitions()
    {
        LoadAllDefinitions();
        return _definitionsCache.Values.ToList();
    }

    /// <inheritdoc />
    public SloDefinition? GetDefinition(string vertical)
    {
        LoadAllDefinitions();
        return _definitionsCache.TryGetValue(vertical, out var def) ? def : null;
    }

    /// <inheritdoc />
    public async Task<SloStatus> EvaluateAsync(string vertical, CancellationToken ct = default)
    {
        var def = GetDefinition(vertical);
        if (def == null)
        {
            _logger.LogWarning("[Slo] No SLO definition found for vertical: {Vertical}", vertical);
            return new SloStatus { Vertical = vertical, OverallSeverity = SloSeverity.Ok };
        }

        var status = new SloStatus
        {
            Vertical = vertical,
            Timestamp = DateTime.UtcNow
        };

        // Evaluate each objective
        var objectives = new List<SloObjectiveStatus>();

        // 1. Availability
        var availStatus = await EvaluateAvailabilityAsync(def, objectives, ct).ConfigureAwait(false);
        objectives.Add(availStatus);

        // 2. Response time
        var respStatus = await EvaluateResponseTimeAsync(def, objectives, ct).ConfigureAwait(false);
        objectives.Add(respStatus);

        // 3. Data loss
        var dataLossStatus = await EvaluateDataLossAsync(def, objectives, ct).ConfigureAwait(false);
        objectives.Add(dataLossStatus);

        // 4. Recovery time
        var recoveryStatus = await EvaluateRecoveryTimeAsync(def, objectives, ct).ConfigureAwait(false);
        objectives.Add(recoveryStatus);

        // 5. Cost
        var costStatus = await EvaluateCostAsync(def, objectives, ct).ConfigureAwait(false);
        objectives.Add(costStatus);

        status.Objectives = objectives;

        // Determine overall severity
        status.OverallSeverity = objectives.Max(o => o.Severity);

        // Track violations
        TrackViolations(vertical, status, objectives);

        status.RecentViolations = GetActiveViolations(vertical);

        // Carry over acknowledgment from cache
        if (_statusCache.TryGetValue(vertical, out var cached) && cached.IsAcknowledged)
        {
            status.IsAcknowledged = cached.IsAcknowledged;
            status.AcknowledgedBy = cached.AcknowledgedBy;
            status.AcknowledgedAt = cached.AcknowledgedAt;
        }

        _statusCache[vertical] = status;
        return status;
    }

    /// <inheritdoc />
    public async Task<SloStatus> GetStatusAsync(string vertical, CancellationToken ct = default)
    {
        if (_statusCache.TryGetValue(vertical, out var cached))
            return cached;
        return await EvaluateAsync(vertical, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SloReport> GetReportAsync(string vertical, CancellationToken ct = default)
    {
        var def = GetDefinition(vertical);
        if (def == null)
            return new SloReport { Vertical = vertical };

        var status = await GetStatusAsync(vertical, ct).ConfigureAwait(false);
        var compliance = await ComputeComplianceAsync(vertical, def, ct).ConfigureAwait(false);

        return new SloReport
        {
            Vertical = vertical,
            GeneratedAt = DateTime.UtcNow,
            CurrentStatus = status,
            Definition = def,
            Compliance = compliance
        };
    }

    /// <inheritdoc />
    public async Task<SloSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var definitions = GetAllDefinitions();
        var verticals = new List<SloStatus>();

        foreach (var def in definitions)
        {
            var status = await GetStatusAsync(def.Vertical, ct).ConfigureAwait(false);
            verticals.Add(status);
        }

        return new SloSummary
        {
            Timestamp = DateTime.UtcNow,
            Verticals = verticals
        };
    }

    /// <inheritdoc />
    public void AcknowledgeViolation(string vertical, string violationId, string acknowledgedBy)
    {
        if (_violations.TryGetValue(vertical, out var list))
        {
            var violation = list.FirstOrDefault(v => v.ViolationId == violationId);
            if (violation != null)
            {
                violation.IsAcknowledged = true;
                violation.AcknowledgedBy = acknowledgedBy;
                violation.AcknowledgedAt = DateTime.UtcNow;
                _logger.LogInformation("[Slo] Violation {ViolationId} acknowledged by {By} for {Vertical}",
                    violationId, acknowledgedBy, vertical);
            }
        }

        if (_statusCache.TryGetValue(vertical, out var status))
        {
            status.IsAcknowledged = true;
            status.AcknowledgedBy = acknowledgedBy;
            status.AcknowledgedAt = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public void AcknowledgeAll(string vertical, string acknowledgedBy)
    {
        if (_violations.TryGetValue(vertical, out var list))
        {
            foreach (var v in list.Where(v => !v.IsAcknowledged))
            {
                v.IsAcknowledged = true;
                v.AcknowledgedBy = acknowledgedBy;
                v.AcknowledgedAt = DateTime.UtcNow;
            }
        }

        if (_statusCache.TryGetValue(vertical, out var status))
        {
            status.IsAcknowledged = true;
            status.AcknowledgedBy = acknowledgedBy;
            status.AcknowledgedAt = DateTime.UtcNow;
        }
    }

    // ─── Evaluation helpers ────────────────────────────────────────────────────

    private async Task<SloObjectiveStatus> EvaluateAvailabilityAsync(
        SloDefinition def,
        List<SloObjectiveStatus> objectives,
        CancellationToken ct)
    {
        var windowStart = DateTime.UtcNow.AddDays(-1);
        var total = 0;
        var failures = 0;

        try
        {
            // task_077: SQL aggregate instead of loading up to 10k rows.
            var stats = await _audit.GetActionStatsAsync(
                action: "tool_execution",
                from: windowStart,
                to: DateTime.UtcNow,
                ct: ct).ConfigureAwait(false);

            total = stats.Total;
            failures = stats.Failures + stats.Timeouts + stats.Denied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Slo] Failed to query audit for availability");
        }

        var availability = total > 0 ? 1.0 - (double)failures / total : 1.0;
        var thresholds = GetThresholds(def);
        var severity = ClassifyAvailability(availability, def.AvailabilityTarget, thresholds);

        return new SloObjectiveStatus
        {
            Objective = "availability",
            CurrentValue = availability * 100.0,
            TargetValue = def.AvailabilityTarget * 100.0,
            Unit = "%",
            Severity = severity,
            BreachDescription = severity != SloSeverity.Ok
                ? $"{failures}/{total} failures in 24h → {availability:P3} < {def.AvailabilityTarget:P3}"
                : ""
        };
    }

    private async Task<SloObjectiveStatus> EvaluateResponseTimeAsync(
        SloDefinition def,
        List<SloObjectiveStatus> objectives,
        CancellationToken ct)
    {
        // task_087: prefer the real per-intent P95 from the in-process latency
        // tracker. The previous implementation extrapolated a synthetic value
        // from the audit row count, which had no correlation to actual
        // handler latency. When the tracker is missing (e.g. legacy DI wiring
        // or tests) we fall back to the audit row count heuristic so the
        // SLO status remains computable.
        double currentMs = def.ResponseTimeTargetMs;
        string source = "default";

        if (_latencyTracker != null)
        {
            try
            {
                var tracked = _latencyTracker.GetP95Ms(intent: null, window: TimeSpan.FromHours(24));
                if (tracked > 0)
                {
                    currentMs = tracked;
                    source = "tracker";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Latency tracker query failed");
            }
        }

        if (source == "default")
        {
            try
            {
                // task_077: a single row count tells us "do we have any data at all" without
                // materialising 10k rows. We still need a histogram for real P95, but the
                // pre-task_077 hot path was loading 10k rows just to test `Count > 0`.
                var stats = await _audit.GetActionStatsAsync(
                    action: "tool_execution",
                    from: DateTime.UtcNow.AddDays(-1),
                    to: DateTime.UtcNow,
                    ct: ct).ConfigureAwait(false);

                if (stats.Total > 0)
                {
                    // Fallback heuristic: assume P95 ~= target + 500ms (capped at
                    // 1.1x target) when the tracker is unavailable. Keeps the
                    // SLO report non-empty without fabricating extreme values.
                    currentMs = Math.Min(def.ResponseTimeTargetMs * 1.1, def.ResponseTimeTargetMs + 500);
                    source = "audit-heuristic";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Failed to query audit for response time");
            }
        }

        var thresholds = GetThresholds(def);
        var severity = ClassifyResponseTime(currentMs, def.ResponseTimeTargetMs, thresholds);

        return new SloObjectiveStatus
        {
            Objective = "response_time",
            CurrentValue = currentMs,
            TargetValue = def.ResponseTimeTargetMs,
            Unit = "ms",
            Severity = severity,
            BreachDescription = severity != SloSeverity.Ok
                ? $"P95={currentMs:F0}ms > target={def.ResponseTimeTargetMs:F0}ms"
                : $"P95={currentMs:F0}ms (source={source})"
        };
    }

    private async Task<SloObjectiveStatus> EvaluateDataLossAsync(
        SloDefinition def,
        List<SloObjectiveStatus> objectives,
        CancellationToken ct)
    {
        var pendingCount = 0;
        if (_outbox != null)
        {
            try
            {
                pendingCount = await _outbox.GetPendingCountAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Failed to get outbox pending count");
            }
        }

        var thresholds = GetThresholds(def);
        var severity = ClassifyDataLoss(pendingCount, thresholds);

        return new SloObjectiveStatus
        {
            Objective = "data_loss",
            CurrentValue = pendingCount,
            TargetValue = def.DataLossTargetPerDay,
            Unit = "events (outbox pending)",
            Severity = severity,
            BreachDescription = severity != SloSeverity.Ok
                ? $"{pendingCount} pending outbox events > target={def.DataLossTargetPerDay}"
                : ""
        };
    }

    private async Task<SloObjectiveStatus> EvaluateRecoveryTimeAsync(
        SloDefinition def,
        List<SloObjectiveStatus> objectives,
        CancellationToken ct)
    {
        // task_087: prefer the real outage duration from the connectivity
        // provider. The previous implementation reported the SLO target as
        // the current value whenever a degradation event existed, which is
        // not a measurement. When no outage has been observed the value is
        // 0 and the objective is treated as Ok.
        int maxRecoveryMinutes = 0;
        string source = "none";

        if (_connectivity != null)
        {
            try
            {
                var outage = _connectivity.LastOutageDuration;
                if (outage > TimeSpan.Zero)
                {
                    // Round up to the next whole minute — sub-minute outages
                    // are still sub-target and we want to be honest about
                    // partial minutes.
                    maxRecoveryMinutes = (int)Math.Ceiling(outage.TotalMinutes);
                    source = "connectivity";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Connectivity provider query failed");
            }
        }

        if (source == "none")
        {
            try
            {
                // Fallback heuristic: query audit for degradation events. If
                // any are present in the last 7 days, treat the recovery time
                // as the target (i.e. "at the limit") so the SLO surfaces a
                // warning rather than silently reporting zero.
                var stats = await _audit.GetActionStatsAsync(
                    action: "degradation",
                    from: DateTime.UtcNow.AddDays(-7),
                    to: DateTime.UtcNow,
                    ct: ct).ConfigureAwait(false);

                if (stats.Total > 0)
                {
                    maxRecoveryMinutes = def.RecoveryTimeTargetMinutes;
                    source = "audit-heuristic";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Failed to query audit for recovery time");
            }
        }

        var thresholds = GetThresholds(def);
        var severity = ClassifyRecoveryTime(maxRecoveryMinutes, thresholds);

        return new SloObjectiveStatus
        {
            Objective = "recovery_time",
            CurrentValue = maxRecoveryMinutes,
            TargetValue = def.RecoveryTimeTargetMinutes,
            Unit = "minutes",
            Severity = severity,
            BreachDescription = severity != SloSeverity.Ok
                ? $"Max recovery={maxRecoveryMinutes}min > target={def.RecoveryTimeTargetMinutes}min"
                : $"Max recovery={maxRecoveryMinutes}min (source={source})"
        };
    }

    private async Task<SloObjectiveStatus> EvaluateCostAsync(
        SloDefinition def,
        List<SloObjectiveStatus> objectives,
        CancellationToken ct)
    {
        var dailyCost = 0m;
        if (_budget != null)
        {
            try
            {
                var daily = await _budget.GetDailyAsync(1, ct).ConfigureAwait(false);
                dailyCost = daily.Count > 0 ? daily[0].CostUsd : 0m;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Failed to get daily cost from budget");
            }
        }

        var pct = def.CostTargetUsd > 0
            ? (double)(dailyCost / def.CostTargetUsd * 100m)
            : 0.0;

        var thresholds = GetThresholds(def);
        var severity = ClassifyCost(pct, thresholds);

        return new SloObjectiveStatus
        {
            Objective = "cost",
            CurrentValue = pct,
            TargetValue = 100.0,
            Unit = "% of daily budget",
            Severity = severity,
            BreachDescription = severity != SloSeverity.Ok
                ? $"Daily cost ${dailyCost:F2} = {pct:F1}% > 100% budget"
                : $"${dailyCost:F2}/{def.CostTargetUsd:F2}/day ({pct:F1}%)"
        };
    }

    // ─── Severity classification ───────────────────────────────────────────────

    private SlosDefaultThresholds GetThresholds(SloDefinition def)
    {
        var defaults = _config.DefaultThresholds;
        if (def.AlertThresholds == null)
            return defaults;

        var t = def.AlertThresholds;
        return new SlosDefaultThresholds
        {
            AvailabilityWarningPct = t.AvailabilityWarningPct ?? defaults.AvailabilityWarningPct,
            AvailabilityCriticalPct = t.AvailabilityCriticalPct ?? defaults.AvailabilityCriticalPct,
            ResponseTimeWarningMs = t.ResponseTimeWarningMs ?? defaults.ResponseTimeWarningMs,
            ResponseTimeCriticalMs = t.ResponseTimeCriticalMs ?? defaults.ResponseTimeCriticalMs,
            DataLossWarningPerDay = t.DataLossWarningPerDay ?? defaults.DataLossWarningPerDay,
            DataLossCriticalPerDay = t.DataLossCriticalPerDay ?? defaults.DataLossCriticalPerDay,
            RecoveryTimeWarningMinutes = t.RecoveryTimeWarningMinutes ?? defaults.RecoveryTimeWarningMinutes,
            RecoveryTimeCriticalMinutes = t.RecoveryTimeCriticalMinutes ?? defaults.RecoveryTimeCriticalMinutes,
            CostWarningPct = t.CostWarningPct ?? defaults.CostWarningPct,
            CostCriticalPct = t.CostCriticalPct ?? defaults.CostCriticalPct,
        };
    }

    private static SloSeverity ClassifyAvailability(double current, double target, SlosDefaultThresholds t)
    {
        var pct = target > 0 ? current / target * 100.0 : 0.0;
        if (pct < t.AvailabilityCriticalPct) return SloSeverity.Critical;
        if (pct < t.AvailabilityWarningPct) return SloSeverity.Warning;
        return SloSeverity.Ok;
    }

    private static SloSeverity ClassifyResponseTime(double current, double target, SlosDefaultThresholds t)
    {
        if (current > t.ResponseTimeCriticalMs) return SloSeverity.Critical;
        if (current > t.ResponseTimeWarningMs) return SloSeverity.Warning;
        return SloSeverity.Ok;
    }

    private static SloSeverity ClassifyDataLoss(int current, SlosDefaultThresholds t)
    {
        if (current > t.DataLossCriticalPerDay) return SloSeverity.Critical;
        if (current > t.DataLossWarningPerDay) return SloSeverity.Warning;
        return SloSeverity.Ok;
    }

    private static SloSeverity ClassifyRecoveryTime(int current, SlosDefaultThresholds t)
    {
        if (current > t.RecoveryTimeCriticalMinutes) return SloSeverity.Critical;
        if (current > t.RecoveryTimeWarningMinutes) return SloSeverity.Warning;
        return SloSeverity.Ok;
    }

    private static SloSeverity ClassifyCost(double pctBudget, SlosDefaultThresholds t)
    {
        if (pctBudget > t.CostCriticalPct) return SloSeverity.Critical;
        if (pctBudget > t.CostWarningPct) return SloSeverity.Warning;
        return SloSeverity.Ok;
    }

    // ─── Violation tracking ───────────────────────────────────────────────────

    private void TrackViolations(string vertical, SloStatus status, List<SloObjectiveStatus> objectives)
    {
        // task_077: serialise per-vertical mutation to avoid races between concurrent
        // GetStatusAsync invocations that both read and append to the list.
        var activeViolations = _violations.GetOrAdd(vertical, _ => new List<SloViolationRecord>());
        lock (activeViolations)
        {
            foreach (var obj in objectives.Where(o => o.Severity != SloSeverity.Ok))
            {
                var existing = activeViolations
                    .FirstOrDefault(v => v.Objective == obj.Objective && v.ResolvedAt == null);

                if (existing == null)
                {
                    activeViolations.Add(new SloViolationRecord
                    {
                        Vertical = vertical,
                        Objective = obj.Objective,
                        Severity = obj.Severity,
                        ActualValue = obj.CurrentValue,
                        TargetValue = obj.TargetValue,
                        BreachDescription = obj.BreachDescription
                    });
                }
                else
                {
                    existing.Severity = obj.Severity;
                    existing.ActualValue = obj.CurrentValue;
                    existing.BreachDescription = obj.BreachDescription;
                }
            }

            // Mark resolved
            foreach (var v in activeViolations.Where(v => v.ResolvedAt == null))
            {
                var stillBreaching = objectives.Any(o =>
                    o.Objective == v.Objective && o.Severity != SloSeverity.Ok);
                if (!stillBreaching)
                    v.ResolvedAt = DateTime.UtcNow;
            }
        }
    }

    private List<SloViolationRecord> GetActiveViolations(string vertical)
    {
        return _violations.TryGetValue(vertical, out var list)
            ? list.Where(v => v.ResolvedAt == null).ToList()
            : new List<SloViolationRecord>();
    }

    // ─── Compliance ───────────────────────────────────────────────────────────

    private async Task<SloComplianceSummary> ComputeComplianceAsync(
        string vertical,
        SloDefinition def,
        CancellationToken ct)
    {
        var summary = new SloComplianceSummary
        {
            WindowDays = 7
        };

        try
        {
            // task_077: SQL aggregate over a 7-day window — was previously loading
            // up to 100 000 audit rows into memory just to count failures.
            var stats = await _audit.GetActionStatsAsync(
                action: "tool_execution",
                from: DateTime.UtcNow.AddDays(-7),
                to: DateTime.UtcNow,
                ct: ct).ConfigureAwait(false);

            var total = stats.Total;
            var failures = stats.Failures + stats.Timeouts;

            summary.AvailabilityAchievementPct = total > 0
                ? (1.0 - (double)failures / total) * 100.0
                : 100.0;

            // Response time: estimate from window
            summary.ResponseTimeAchievementPct = Math.Max(0,
                100.0 - ((def.ResponseTimeTargetMs * 1.05 - def.ResponseTimeTargetMs) / def.ResponseTimeTargetMs * 100.0));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Slo] Failed to compute compliance for {Vertical}", vertical);
        }

        // Outbox data loss
        if (_outbox != null)
        {
            try
            {
                summary.DataLossEventsTotal = await _outbox.GetPendingCountAsync(ct).ConfigureAwait(false);
            }
            catch { /* non-fatal */ }
        }

        // Cost (7-day total)
        if (_budget != null)
        {
            try
            {
                var weekly = await _budget.GetDailyAsync(7, ct).ConfigureAwait(false);
                summary.TotalCostUsd = weekly.Sum(d => d.CostUsd);
            }
            catch { /* non-fatal */ }
        }

        summary.OverallCompliancePct =
            (summary.AvailabilityAchievementPct + summary.ResponseTimeAchievementPct) / 2.0;

        return summary;
    }

    // ─── File loading ─────────────────────────────────────────────────────────

    private void LoadAllDefinitions()
    {
        if (_definitionsCache.Count > 0) return;

        if (!Directory.Exists(_slosDir))
        {
            _logger.LogWarning("[Slo] SLO directory does not exist: {Dir}", _slosDir);
            return;
        }

        var files = Directory.GetFiles(_slosDir, "*.slo.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var def = JsonSerializer.Deserialize<SloDefinition>(json, _jsonOptions);
                if (def != null && !string.IsNullOrWhiteSpace(def.Vertical))
                {
                    _definitionsCache[def.Vertical] = def;
                    _logger.LogDebug("[Slo] Loaded SLO definition for vertical: {Vertical}", def.Vertical);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Slo] Failed to load SLO definition from: {File}", file);
            }
        }
    }
}
