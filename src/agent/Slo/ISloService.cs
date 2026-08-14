namespace Hercules.Slo;

/// <summary>
///     Service for reading, evaluating and reporting on operational SLOs (task_064).
/// </summary>
public interface ISloService
{
    /// <summary>Absolute path to the SLO definitions directory.</summary>
    string GetSlosDir();

    /// <summary>Load all SLO definitions from the SlosDir.</summary>
    IReadOnlyList<SloDefinition> GetAllDefinitions();

    /// <summary>Load a specific vertical SLO definition.</summary>
    SloDefinition? GetDefinition(string vertical);

    /// <summary>
    ///     Evaluate current metric values against a vertical's SLO targets.
    ///     Returns a status snapshot with objective-level severity.
    ///     task_077: async — performs SQL aggregate, outbox/budget I/O without blocking the thread-pool.
    /// </summary>
    Task<SloStatus> EvaluateAsync(string vertical, CancellationToken ct = default);

    /// <summary>
    ///     Get the current SLO status for a vertical (cached between evaluations).
    ///     task_077: async.
    /// </summary>
    Task<SloStatus> GetStatusAsync(string vertical, CancellationToken ct = default);

    /// <summary>
    ///     Get a full SLO report for a vertical (status + definition + compliance summary).
    ///     task_077: async.
    /// </summary>
    Task<SloReport> GetReportAsync(string vertical, CancellationToken ct = default);

    /// <summary>
    ///     Get a summary of all verticals' current SLO status.
    ///     task_077: async.
    /// </summary>
    Task<SloSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    ///     Acknowledge an active violation, suppressing repeat alerts.
    /// </summary>
    void AcknowledgeViolation(string vertical, string violationId, string acknowledgedBy);

    /// <summary>
    ///     Acknowledge all active violations for a vertical.
    /// </summary>
    void AcknowledgeAll(string vertical, string acknowledgedBy);
}
