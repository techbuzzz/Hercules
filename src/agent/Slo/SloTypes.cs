using System.Text.Json.Serialization;

namespace Hercules.Slo;

/// <summary>
///     Operational SLO definition for a specific vertical (task_064).
///     Each .slo.json file defines targets, alert thresholds, and runbooks.
/// </summary>
public sealed class SloDefinition
{
    /// <summary>Вертикаль (например: greenhouse, cold-chain, server-room, vending).</summary>
    public string Vertical { get; set; } = "";

    /// <summary>Описание вертикали.</summary>
    public string Description { get; set; } = "";

    /// <summary>Версия SLO definition. Default: "1.0".</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Availability SLO target (decimal, e.g. 0.999 = 99.9%).</summary>
    public double AvailabilityTarget { get; set; } = 0.999;

    /// <summary>Response time SLO target: P95 latency in milliseconds.</summary>
    public double ResponseTimeTargetMs { get; set; } = 1000;

    /// <summary>Data loss SLO target: maximum lost events per day.</summary>
    public int DataLossTargetPerDay { get; set; } = 0;

    /// <summary>Recovery time SLO target: maximum minutes to recover from outage.</summary>
    public int RecoveryTimeTargetMinutes { get; set; } = 10;

    /// <summary>Daily cost budget target in USD.</summary>
    public decimal CostTargetUsd { get; set; } = 10m;

    /// <summary>Alert thresholds override (uses config defaults if absent).</summary>
    public SloAlertThresholds? AlertThresholds { get; set; }

    /// <summary>Runbooks for each SLO breach type.</summary>
    public SloRunbooks Runbooks { get; set; } = new();
}

/// <summary>
///     Alert thresholds for a specific vertical (overrides config defaults).
/// </summary>
public sealed class SloAlertThresholds
{
    /// <summary>Warning: availability drops below this % of target.</summary>
    public double? AvailabilityWarningPct { get; set; }

    /// <summary>Critical: availability drops below this % of target.</summary>
    public double? AvailabilityCriticalPct { get; set; }

    /// <summary>Warning: P95 response time exceeds this ms.</summary>
    public double? ResponseTimeWarningMs { get; set; }

    /// <summary>Critical: P95 response time exceeds this ms.</summary>
    public double? ResponseTimeCriticalMs { get; set; }

    /// <summary>Warning: data loss exceeds this events/day.</summary>
    public int? DataLossWarningPerDay { get; set; }

    /// <summary>Critical: data loss exceeds this events/day.</summary>
    public int? DataLossCriticalPerDay { get; set; }

    /// <summary>Warning: recovery time exceeds this minutes.</summary>
    public int? RecoveryTimeWarningMinutes { get; set; }

    /// <summary>Critical: recovery time exceeds this minutes.</summary>
    public int? RecoveryTimeCriticalMinutes { get; set; }

    /// <summary>Warning: cost exceeds this % of daily budget.</summary>
    public double? CostWarningPct { get; set; }

    /// <summary>Critical: cost exceeds this % of daily budget.</summary>
    public double? CostCriticalPct { get; set; }
}

/// <summary>
///     Runbooks for SLO breach scenarios.
/// </summary>
public sealed class SloRunbooks
{
    /// <summary>Runbook for availability breach.</summary>
    public SloRunbook Availability { get; set; } = new();

    /// <summary>Runbook for response time breach.</summary>
    public SloRunbook ResponseTime { get; set; } = new();

    /// <summary>Runbook for data loss breach.</summary>
    public SloRunbook DataLoss { get; set; } = new();

    /// <summary>Runbook for recovery time breach.</summary>
    public SloRunbook RecoveryTime { get; set; } = new();

    /// <summary>Runbook for cost budget breach.</summary>
    public SloRunbook Cost { get; set; } = new();
}

/// <summary>
///     Runbook entry for a specific SLO breach type.
/// </summary>
public sealed class SloRunbook
{
    /// <summary>Краткое описание проблемы.</summary>
    public string Title { get; set; } = "";

    /// <summary>Симптомы для диагностики.</summary>
    public List<string> Symptoms { get; set; } = new();

    /// <summary>Шаги для диагностики.</summary>
    public List<string> DiagnosisSteps { get; set; } = new();

    /// <summary>Шаги для mitigation.</summary>
    public List<string> MitigationSteps { get; set; } = new();

    /// <summary>Когда эскалировать (escalate).</summary>
    public string EscalationTrigger { get; set; } = "";
}

/// <summary>
///     Severity level for an SLO violation.
/// </summary>
public enum SloSeverity
{
    Ok,
    Warning,
    Critical
}

/// <summary>
///     Individual SLO objective status.
/// </summary>
public sealed class SloObjectiveStatus
{
    public string Objective { get; set; } = "";
    public SloSeverity Severity { get; set; } = SloSeverity.Ok;
    public double CurrentValue { get; set; }
    public double TargetValue { get; set; }
    public string Unit { get; set; } = "";

    /// <summary>Percentage of target achieved (current/target * 100). Capped at 200.</summary>
    public double TargetAchievementPct => TargetValue > 0
        ? Math.Min(CurrentValue / TargetValue * 100.0, 200.0)
        : 0.0;

    public string BreachDescription { get; set; } = "";
}

/// <summary>
///     Overall SLO status for a vertical.
/// </summary>
public sealed class SloStatus
{
    public string Vertical { get; set; } = "";
    public SloSeverity OverallSeverity { get; set; } = SloSeverity.Ok;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<SloObjectiveStatus> Objectives { get; set; } = new();
    public List<SloViolationRecord> RecentViolations { get; set; } = new();
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

/// <summary>
///     SLO violation event record.
/// </summary>
public sealed class SloViolationRecord
{
    public string ViolationId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Vertical { get; set; } = "";
    public string Objective { get; set; } = "";
    public SloSeverity Severity { get; set; } = SloSeverity.Ok;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public double ActualValue { get; set; }
    public double TargetValue { get; set; }
    public string BreachDescription { get; set; } = "";
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

/// <summary>
///     SLO report: current status snapshot and historical compliance.
/// </summary>
public sealed class SloReport
{
    public string Vertical { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public SloStatus CurrentStatus { get; set; } = new();
    public SloDefinition Definition { get; set; } = new();
    public SloComplianceSummary Compliance { get; set; } = new();
}

/// <summary>
///     Compliance summary over a time window.
/// </summary>
public sealed class SloComplianceSummary
{
    /// <summary>Number of days in the reporting window.</summary>
    public int WindowDays { get; set; } = 7;

    /// <summary>Achievement % for availability over the window.</summary>
    public double AvailabilityAchievementPct { get; set; }

    /// <summary>Achievement % for response time (inverted: lower is better).</summary>
    public double ResponseTimeAchievementPct { get; set; }

    /// <summary>Total data loss events in the window.</summary>
    public int DataLossEventsTotal { get; set; }

    /// <summary>Maximum observed recovery time in minutes.</summary>
    public int MaxRecoveryTimeMinutes { get; set; }

    /// <summary>Total cost in USD in the window.</summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>Overall compliance: weighted average achievement.</summary>
    public double OverallCompliancePct { get; set; }
}

/// <summary>
///     Summary of all SLOs for a GET /api/slos endpoint.
/// </summary>
public sealed class SloSummary
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<SloStatus> Verticals { get; set; } = new();
    public int TotalVerticals => Verticals.Count;
    public int OkCount => Verticals.Count(v => v.OverallSeverity == SloSeverity.Ok);
    public int WarningCount => Verticals.Count(v => v.OverallSeverity == SloSeverity.Warning);
    public int CriticalCount => Verticals.Count(v => v.OverallSeverity == SloSeverity.Critical);
}
