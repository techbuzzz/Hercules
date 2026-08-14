namespace Hercules.Degradation;

/// <summary>
///     Configuration for local-first degradation behavior (task_061).
///     Defines health checks, fallback strategies, and operator notifications.
/// </summary>
public sealed class DegradationConfig
{
    /// <summary>Enable degradation system. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Health check settings.
    /// </summary>
    public HealthCheckConfig HealthCheck { get; set; } = new();

    /// <summary>
    ///     Fallback strategies configuration.
    /// </summary>
    public FallbackConfig Fallback { get; set; } = new();

    /// <summary>
    ///     Operator notification settings.
    /// </summary>
    public NotificationConfig Notifications { get; set; } = new();

    /// <summary>
    ///     Observability settings.
    /// </summary>
    public ObservabilityConfig Observability { get; set; } = new();
}

/// <summary>
///     Health check configuration for LLM, network, and mesh services.
/// </summary>
public sealed class HealthCheckConfig
{
    /// <summary>Enable health checks. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval between health checks in seconds. Default: 30.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Timeout for health check in seconds. Default: 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Number of consecutive failures before marking service as unhealthy. Default: 3.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>Number of consecutive successes before marking service as healthy. Default: 2.</summary>
    public int RecoveryThreshold { get; set; } = 2;

    /// <summary>Check LLM provider availability. Default: true.</summary>
    public bool CheckLlmProvider { get; set; } = true;

    /// <summary>Check network connectivity. Default: true.</summary>
    public bool CheckNetwork { get; set; } = true;

    /// <summary>Check mesh bus availability. Default: true.</summary>
    public bool CheckMeshBus { get; set; } = true;

    /// <summary>Check skill registry availability. Default: true.</summary>
    public bool CheckSkillRegistry { get; set; } = true;
}

/// <summary>
///     Fallback strategy configuration.
/// </summary>
public sealed class FallbackConfig
{
    /// <summary>Enable deterministic fallback rules. Default: true.</summary>
    public bool EnableDeterministicFallback { get; set; } = true;

    /// <summary>Use local skills when cloud skills unavailable. Default: true.</summary>
    public bool UseLocalSkills { get; set; } = true;

    /// <summary>Use reduced-capability models when primary LLM unavailable. Default: true.</summary>
    public bool UseReducedCapabilityModels { get; set; } = true;

    /// <summary>Queue work when no LLM available. Default: true.</summary>
    public bool QueueWorkWhenOffline { get; set; } = true;

    /// <summary>Allow new delegations in degraded mode. Default: true.</summary>
    public bool AllowDelegationsInDegradedMode { get; set; } = true;

    /// <summary>Refuse new delegations when offline. Default: true.</summary>
    public bool RefuseDelegationsWhenOffline { get; set; } = true;

    /// <summary>Maximum queue size for deferred work. Default: 1000.</summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>List of reduced-capability model identifiers (in order of preference).</summary>
    public List<string> ReducedCapabilityModels { get; set; } = new()
    {
        "gpt-4o-mini",
        "gpt-3.5-turbo",
        "llama3.2:1b"
    };
}

/// <summary>
///     Operator notification configuration.
/// </summary>
public sealed class NotificationConfig
{
    /// <summary>Enable operator notifications. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Notify on mode transition to Degraded. Default: true.</summary>
    public bool NotifyOnDegraded { get; set; } = true;

    /// <summary>Notify on mode transition to Offline. Default: true.</summary>
    public bool NotifyOnOffline { get; set; } = true;

    /// <summary>Notify on recovery to Full mode. Default: true.</summary>
    public bool NotifyOnRecovery { get; set; } = true;

    /// <summary>Notify on new work queued (offline mode). Default: false.</summary>
    public bool NotifyOnWorkQueued { get; set; } = false;

    /// <summary>Webhook URL for notifications. Default: empty.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Telegram bot token. Default: empty.</summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>Telegram chat ID for notifications. Default: empty.</summary>
    public string? TelegramChatId { get; set; }

    /// <summary>Email SMTP server. Default: empty.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>SMTP port. Default: 587.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>SMTP username. Default: empty.</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>SMTP password (should be in secrets). Default: empty.</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>From email address. Default: empty.</summary>
    public string? FromEmail { get; set; }

    /// <summary>To email addresses (comma-separated). Default: empty.</summary>
    public string? ToEmails { get; set; }
}

/// <summary>
///     Observability configuration for degradation state.
/// </summary>
public sealed class ObservabilityConfig
{
    /// <summary>Enable degradation metrics. Default: true.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Enable degradation state logging. Default: true.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Enable degradation status endpoint. Default: true.</summary>
    public bool EnableStatusEndpoint { get; set; } = true;

    /// <summary>Log level for degradation events: Trace, Debug, Information, Warning, Error. Default: Information.</summary>
    public string LogLevel { get; set; } = "Information";
}
