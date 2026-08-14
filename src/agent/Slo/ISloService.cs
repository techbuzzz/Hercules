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
    /// </summary>
    SloStatus Evaluate(string vertical);

    /// <summary>
    ///     Get the current SLO status for a vertical (cached between evaluations).
    /// </summary>
    SloStatus GetStatus(string vertical);

    /// <summary>
    ///     Get a full SLO report for a vertical (status + definition + compliance summary).
    /// </summary>
    SloReport GetReport(string vertical);

    /// <summary>
    ///     Get a summary of all verticals' current SLO status.
    /// </summary>
    SloSummary GetSummary();

    /// <summary>
    ///     Acknowledge an active violation, suppressing repeat alerts.
    /// </summary>
    void AcknowledgeViolation(string vertical, string violationId, string acknowledgedBy);

    /// <summary>
    ///     Acknowledge all active violations for a vertical.
    /// </summary>
    void AcknowledgeAll(string vertical, string acknowledgedBy);
}
