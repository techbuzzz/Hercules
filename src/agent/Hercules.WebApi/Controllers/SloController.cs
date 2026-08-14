using Hercules.Slo;
using Microsoft.AspNetCore.Mvc;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Operational SLO endpoints (task_064).
///     Availability, response-time, data-loss, recovery-time, cost tracking and reporting.
/// </summary>
[ApiController]
[Route("api/slos")]
public sealed class SloController : ControllerBase
{
    private readonly ISloService _slo;

    public SloController(ISloService slo)
    {
        _slo = slo ?? throw new ArgumentNullException(nameof(slo));
    }

    /// <summary>Returns summary of all verticals SLO status.</summary>
    [HttpGet]
    public ActionResult<SloSummary> GetSummary()
    {
        var summary = _slo.GetSummary();
        return Ok(summary);
    }

    /// <summary>Returns the SLO definition for a specific vertical.</summary>
    [HttpGet("{vertical}/definition")]
    public ActionResult<SloDefinition> GetDefinition(string vertical)
    {
        var def = _slo.GetDefinition(vertical);
        if (def == null)
            return NotFound(new { error = $"No SLO definition found for vertical: {vertical}" });
        return Ok(def);
    }

    /// <summary>Returns the current SLO status for a vertical.</summary>
    [HttpGet("{vertical}")]
    public ActionResult<SloStatus> GetStatus(string vertical)
    {
        var status = _slo.GetStatus(vertical);
        return Ok(status);
    }

    /// <summary>Returns a full SLO report for a vertical (status + definition + compliance).</summary>
    [HttpGet("{vertical}/report")]
    public ActionResult<SloReport> GetReport(string vertical)
    {
        var report = _slo.GetReport(vertical);
        if (report.Definition.Vertical == null)
            return NotFound(new { error = $"No SLO definition found for vertical: {vertical}" });
        return Ok(report);
    }

    /// <summary>
    ///     Acknowledge a specific SLO violation (suppresses repeat alerts).
    /// </summary>
    [HttpPost("{vertical}/ack/{violationId}")]
    public ActionResult Acknowledge(string vertical, string violationId, [FromQuery] string? acknowledgedBy = null)
    {
        var who = acknowledgedBy ?? "operator";
        _slo.AcknowledgeViolation(vertical, violationId, who);
        return Ok(new { acknowledged = true, violationId, by = who });
    }

    /// <summary>
    ///     Acknowledge all active SLO violations for a vertical.
    /// </summary>
    [HttpPost("{vertical}/ack")]
    public ActionResult AcknowledgeAll(string vertical, [FromQuery] string? acknowledgedBy = null)
    {
        var who = acknowledgedBy ?? "operator";
        _slo.AcknowledgeAll(vertical, who);
        return Ok(new { acknowledgedAll = true, vertical, by = who });
    }
}
