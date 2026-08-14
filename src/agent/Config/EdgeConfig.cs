namespace Hercules.Config;

/// <summary>
///     Edge provisioning configuration (task_059).
///     Mapped from appsettings.json Edge section.
/// </summary>
public sealed class EdgeConfig
{
    /// <summary>
    ///     Fleet management server URL for identity enrollment.
    ///     If empty, enrollment is local-only (standalone edge device).
    ///     Default: "".
    /// </summary>
    public string EnrolmentUrl { get; set; } = "";

    /// <summary>
    ///     Pre-shared enrollment token (from first-boot provisioning).
    ///     Default: "".
    /// </summary>
    public string EnrolmentToken { get; set; } = "";

    /// <summary>
    ///     Unique hardware device identifier (MAC address or SoC serial).
    ///     Auto-detected by first-boot.sh. Default: "".
    /// </summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    ///     True once enrollment has completed successfully.
    ///     Default: false.
    /// </summary>
    public bool Enrolled { get; set; } = false;

    /// <summary>
    ///     Interval in minutes between enrollment retry attempts.
    ///     Default: 5.
    /// </summary>
    public int EnrolmentRetryIntervalMinutes { get; set; } = 5;

    /// <summary>
    ///     Maximum number of enrollment retry attempts.
    ///     Default: 10.
    /// </summary>
    public int EnrolmentMaxRetries { get; set; } = 10;

    /// <summary>
    ///     Path to the persisted enrollment state file.
    ///     Default: "{DataRoot}/security/enrollment.json".
    /// </summary>
    public string EnrollmentStatePath { get; set; } = "";
}
