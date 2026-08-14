namespace Hercules.Security;

/// <summary>
///     Security audit export service (task_055).
///     Exports security audit logs and compliance reports.
/// </summary>
public interface ISecurityAuditExporter
{
    /// <summary>
    ///     Exports security audit report for a date range.
    /// </summary>
    Task<SecurityAuditExport> ExportAuditReportAsync(
        DateTime from,
        DateTime to,
        AuditExportFormat format = AuditExportFormat.Json,
        CancellationToken ct = default);

    /// <summary>
    ///     Exports compliance report.
    /// </summary>
    Task<ComplianceReport> ExportComplianceReportAsync(
        ComplianceStandard standard,
        CancellationToken ct = default);

    /// <summary>
    ///     Exports identity audit trail.
    /// </summary>
    Task<IdentityAuditTrail> ExportIdentityAuditTrailAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}

/// <summary>
///     Audit export formats.
/// </summary>
public enum AuditExportFormat
{
    Json,
    Csv,
    Syslog
}

/// <summary>
///     Compliance standards.
/// </summary>
public enum ComplianceStandard
{
    SOC2,
    ISO27001,
    GDPR,
    HIPAA
}

/// <summary>
///     Security audit export.
/// </summary>
public sealed record SecurityAuditExport(
    string ReportId,
    DateTime GeneratedAt,
    DateTime From,
    DateTime To,
    AuditExportFormat Format,
    int TotalEvents,
    IReadOnlyList<SecurityEvent> Events,
    SecurityMetrics Metrics);

/// <summary>
///     Security event entry.
/// </summary>
public sealed record SecurityEvent(
    string EventId,
    string EventType,
    string Actor,
    string? Target,
    DateTime Timestamp,
    string? Details,
    string? IpAddress,
    bool Success);

/// <summary>
///     Security metrics summary.
/// </summary>
public sealed record SecurityMetrics(
    int TotalEvents,
    int AuthenticationEvents,
    int AuthorizationEvents,
    int ConfigurationChanges,
    int ToolExecutions,
    int SkillOperations,
    int SecurityAlerts,
    double AverageResponseTimeMs);

/// <summary>
///     Compliance report.
/// </summary>
public sealed record ComplianceReport(
    string ReportId,
    ComplianceStandard Standard,
    DateTime GeneratedAt,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool IsCompliant,
    IReadOnlyList<ComplianceCheck> Checks,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Recommendations);

/// <summary>
///     Individual compliance check result.
/// </summary>
public sealed record ComplianceCheck(
    string ControlId,
    string Description,
    bool Passed,
    string? Evidence,
    string? Remediation);

/// <summary>
///     Identity audit trail.
/// </summary>
public sealed record IdentityAuditTrail(
    string AgentId,
    DateTime ExportedAt,
    DateTime From,
    DateTime To,
    IReadOnlyList<IdentityEvent> Events);

/// <summary>
///     Identity event.
/// </summary>
public sealed record IdentityEvent(
    string EventId,
    string EventType,
    string CredentialId,
    DateTime Timestamp,
    string? Details);
