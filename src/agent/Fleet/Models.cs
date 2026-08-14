using System.Text.Json.Serialization;

namespace Hercules.Fleet;

/// <summary>
///     Fleet template manifest — describes a deployable fleet template for a vertical domain.
///     Bundled inside <c>*.fleettemplate</c> ZIP archives alongside agent template, monitoring, policy and offline configs.
/// </summary>
public sealed class FleetManifest
{
    /// <summary>Human-readable name, e.g. "Greenhouse Assistant Fleet".</summary>
    public string Name { get; set; } = "";

    /// <summary>Brief description of the fleet template.</summary>
    public string Description { get; set; } = "";

    /// <summary>Template schema version. Current: 1.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    ///     Vertical domain. Must match one of the agent template scenarios
    ///     (greenhouse, cold-chain, server-room, vending, energy).
    /// </summary>
    public string Vertical { get; set; } = "";

    /// <summary>Agent template file to apply as part of this fleet template, e.g. "greenhouse.agenttemplate".</summary>
    public string AgentTemplateFile { get; set; } = "";

    /// <summary>Hardware bill-of-materials for a typical deployment node.</summary>
    public HardwareBom HardwareBom { get; set; } = new();

    /// <summary>Fleet-level monitoring configuration (metric thresholds, alert rules, log level).</summary>
    public MonitoringConfig Monitoring { get; set; } = new();

    /// <summary>Fleet-level policy: delegation, concurrency, agent count limits.</summary>
    public FleetPolicy Policy { get; set; } = new();

    /// <summary>Default offline/degradation behaviour for edge nodes.</summary>
    public OfflineDefaults Offline { get; set; } = new();
}

/// <summary>
///     Hardware bill-of-materials — recommended specs for a single fleet node.
/// </summary>
public sealed class HardwareBom
{
    /// <summary>CPU description, e.g. "ARM Cortex-A72 4-core @ 1.5GHz".</summary>
    public string Cpu { get; set; } = "";

    /// <summary>Minimum RAM in GB.</summary>
    public int RamGb { get; set; }

    /// <summary>Minimum storage in GB.</summary>
    public int StorageGb { get; set; }

    /// <summary>Storage type hint, e.g. "SSD (microSD A2)".</summary>
    public string StorageType { get; set; } = "";

    /// <summary>Network interfaces, e.g. "Ethernet 1 Gbps + Wi-Fi 802.11ac".</summary>
    public string Network { get; set; } = "";

    /// <summary>Power consumption in watts.</summary>
    public int PowerWatts { get; set; }

    /// <summary>Recommended operating temperature range.</summary>
    public string OperatingTemp { get; set; } = "";

    /// <summary>Form factor / enclosure hint.</summary>
    public string FormFactor { get; set; } = "";

    /// <summary>Supported sensors or actuators for this vertical.</summary>
    public List<string> SupportedSensors { get; set; } = new();

    /// <summary>Optional notes, e.g. "Raspberry Pi 4B recommended".</summary>
    public string Notes { get; set; } = "";
}

/// <summary>
///     Fleet-level monitoring configuration.
/// </summary>
public sealed class MonitoringConfig
{
    /// <summary>Metric alert thresholds (metric name → threshold config).</summary>
    public Dictionary<string, MetricThreshold> MetricThresholds { get; set; } = new();

    /// <summary>Named alert rules with condition and severity.</summary>
    public List<AlertRule> AlertRules { get; set; } = new();

    /// <summary>Minimum log level for fleet nodes.</summary>
    public string MinLogLevel { get; set; } = "Information";

    /// <summary>Export interval for metrics in seconds.</summary>
    public int MetricsExportIntervalSeconds { get; set; } = 30;
}

/// <summary>
///     Threshold for a single metric (e.g. CPU %, memory %, latency ms).
/// </summary>
public sealed class MetricThreshold
{
    /// <summary>Warning threshold value.</summary>
    public double Warning { get; set; }

    /// <summary>Critical threshold value.</summary>
    public double Critical { get; set; }

    /// <summary>Unit label, e.g. "%", "ms", "MB".</summary>
    public string Unit { get; set; } = "";

    /// <summary>Evaluation window in seconds (rolling average).</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
///     A named alert rule with a simple condition expression and severity.
/// </summary>
public sealed class AlertRule
{
    /// <summary>Unique rule identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = "";

    /// <summary>Severity: Info, Warning, Critical.</summary>
    public string Severity { get; set; } = "Warning";

    /// <summary>
    ///     Simple condition expression, e.g. "cpu_pct > 90", "memory_pct > 85", "task_queue_depth > 50".
    /// </summary>
    public string Condition { get; set; } = "";

    /// <summary>Cooldown in seconds before re-alerting.</summary>
    public int CooldownSeconds { get; set; } = 300;
}

/// <summary>
///     Fleet-level policy constraints.
/// </summary>
public sealed class FleetPolicy
{
    /// <summary>Maximum number of agents in the fleet.</summary>
    public int MaxAgents { get; set; } = 10;

    /// <summary>Maximum delegation hops.</summary>
    public int MaxDelegationHops { get; set; } = 3;

    /// <summary>Maximum concurrent tasks per agent.</summary>
    public int MaxConcurrentTasksPerAgent { get; set; } = 5;

    /// <summary>Maximum fan-out width for orchestrator tasks.</summary>
    public int MaxFanOutWidth { get; set; } = 8;

    /// <summary>Require inter-agent authentication.</summary>
    public bool RequireMutualTls { get; set; } = false;

    /// <summary>Require signed task payloads.</summary>
    public bool RequireSignedPayloads { get; set; } = false;
}

/// <summary>
///     Default offline/degradation behaviour for edge nodes using this fleet template.
/// </summary>
public sealed class OfflineDefaults
{
    /// <summary>Default degradation mode: Full / Degraded / Offline.</summary>
    public string DefaultDegradationMode { get; set; } = "Degraded";

    /// <summary>List of skill IDs allowed when fully offline.</summary>
    public List<string> AllowedOfflineSkills { get; set; } = new();

    /// <summary>Enable offline outbox queue for task results.</summary>
    public bool EnableOutboxQueue { get; set; } = true;

    /// <summary>Sync interval in seconds when network is available.</summary>
    public int SyncIntervalSeconds { get; set; } = 60;

    /// <summary>Enable queued work execution when offline.</summary>
    public bool EnableQueuedWork { get; set; } = true;

    /// <summary>Notification webhook URL for offline events.</summary>
    public string? OfflineNotificationUrl { get; set; }
}

/// <summary>
///     Conflict resolution strategy when fleet config already exists.
/// </summary>
public enum FleetConflictResolution
{
    /// <summary>Skip already-existing config files.</summary>
    Skip,

    /// <summary>Rename existing config to *.bak before writing new.</summary>
    Rename,

    /// <summary>Throw if any conflict detected.</summary>
    Fail
}

/// <summary>
///     Result of applying a fleet template.
/// </summary>
public sealed class ApplyFleetTemplateResult
{
    public string TemplateName { get; set; } = "";
    public string Vertical { get; set; } = "";
    public bool AgentTemplateApplied { get; set; }
    public List<string> InstalledConfigFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
///     Entry returned by the List() operation.
/// </summary>
public sealed record FleetTemplateEntry(
    string FileName,
    string Name,
    string Description,
    string Vertical,
    int Version,
    string FilePath);
