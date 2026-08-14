namespace Hercules.Edge;

/// <summary>
///     Edge provisioning service (task_059).
///     Handles first-boot identity enrolment, certificate activation, and secure defaults
///     on Raspberry Pi / edge devices.
/// </summary>
public interface IEdgeProvisioningService
{
    /// <summary>
    ///     Returns true if this device has already completed enrollment.
    /// </summary>
    bool IsEnrolled { get; }

    /// <summary>
    ///     Performs enrollment if not already enrolled.
    ///     Activates fleet identity, generates/rotates certificates, writes secure defaults.
    ///     Idempotent — safe to call multiple times.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>EnrollmentResult describing what happened.</returns>
    Task<EnrolmentResult> EnsureEnrolledAsync(CancellationToken ct = default);

    /// <summary>
    ///     Marks the device as successfully enrolled (persisted to disk).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task MarkEnrolledAsync(CancellationToken ct = default);

    /// <summary>
    ///     Returns current device identity info, or null if not enrolled.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<DeviceIdentity?> GetDeviceIdentityAsync(CancellationToken ct = default);
}

/// <summary>
///     Result of an enrollment attempt.
/// </summary>
public sealed record EnrolmentResult(
    bool Success,
    bool WasAlreadyEnrolled,
    string? ErrorMessage,
    string? DeviceId,
    string? EnrolmentToken)
{
    public static EnrolmentResult AlreadyEnrolled(string deviceId) =>
        new(Success: true, WasAlreadyEnrolled: true, ErrorMessage: null, DeviceId: deviceId, EnrolmentToken: null);

    public static EnrolmentResult Succeeded(string deviceId, string token) =>
        new(Success: true, WasAlreadyEnrolled: false, ErrorMessage: null, DeviceId: deviceId, EnrolmentToken: token);

    public static EnrolmentResult Failed(string message) =>
        new(Success: false, WasAlreadyEnrolled: false, ErrorMessage: message, DeviceId: null, EnrolmentToken: null);
}

/// <summary>
///     Device identity information persisted after enrollment.
/// </summary>
public sealed record DeviceIdentity(
    string DeviceId,
    string EnrolmentToken,
    DateTime EnrolledAt,
    string? FleetEndpoint);
