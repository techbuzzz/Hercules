using Hercules.Security;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     HTTP-эндпоинты security operations (Phase 7 task_095).
///     Тонкая обёртка поверх уже существующих сервисов <see cref="IVulnerabilityReporter" />
///     и <see cref="ISecurityAuditExporter" /> из task_055 — task_055 реализовал
///     только сервисы, без HTTP-уровня, поэтому web-UI не мог к ним достучаться.
///     Закрывает пробел между backend-сервисами и web-панелью SecurityOpsPanel.
/// </summary>
public static class SecurityOpsController
{
    public static void MapSecurityOps(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/security").WithTags("Security");

        // GET /api/security/vulnerabilities — список уязвимостей с фильтрами.
        group.MapGet("/vulnerabilities", async (
            IVulnerabilityReporter reporter,
            string? minSeverity = null,
            string? status = null,
            string? component = null,
            DateTime? from = null,
            DateTime? to = null,
            int? limit = null,
            CancellationToken ct = default) =>
        {
            VulnerabilityFilter? filter = null;
            if (!string.IsNullOrEmpty(minSeverity)
                && Enum.TryParse<VulnerabilitySeverity>(minSeverity, true, out var sev))
            {
                filter = (filter ?? new VulnerabilityFilter()) with { MinSeverity = sev };
            }
            if (!string.IsNullOrEmpty(status)
                && Enum.TryParse<VulnerabilityStatus>(status, true, out var st))
            {
                filter = (filter ?? new VulnerabilityFilter()) with { Status = st };
            }
            if (!string.IsNullOrEmpty(component))
            {
                filter = (filter ?? new VulnerabilityFilter()) with { AffectedComponent = component };
            }
            if (from.HasValue)
            {
                filter = (filter ?? new VulnerabilityFilter()) with { FromDate = from };
            }
            if (to.HasValue)
            {
                filter = (filter ?? new VulnerabilityFilter()) with { ToDate = to };
            }

            var items = await reporter.GetVulnerabilitiesAsync(filter, ct);
            if (limit.HasValue && limit.Value > 0 && items.Count > limit.Value)
            {
                items = items.Take(limit.Value).ToList();
            }

            return Results.Ok(new { count = items.Count, vulnerabilities = items });
        }).WithName("ListVulnerabilities");

        // GET /api/security/vulnerabilities/summary — агрегированная статистика.
        group.MapGet("/vulnerabilities/summary", async (IVulnerabilityReporter reporter, CancellationToken ct) =>
        {
            var summary = await reporter.GetSummaryAsync(ct);
            return Results.Ok(summary);
        }).WithName("VulnerabilitySummary");

        // GET /api/security/vulnerabilities/{id} — конкретная уязвимость.
        group.MapGet("/vulnerabilities/{id}", async (string id, IVulnerabilityReporter reporter, CancellationToken ct) =>
        {
            var vuln = await reporter.GetVulnerabilityAsync(id, ct);
            return vuln is null
                ? Results.NotFound(new { error = $"Vulnerability '{id}' not found." })
                : Results.Ok(vuln);
        }).WithName("GetVulnerability");

        // PATCH /api/security/vulnerabilities/{id}/status — обновить статус.
        group.MapPatch("/vulnerabilities/{id}/status", async (
            string id,
            UpdateVulnerabilityStatusRequest body,
            IVulnerabilityReporter reporter,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<VulnerabilityStatus>(body.Status, true, out var newStatus))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown status: {body.Status}. Valid: {string.Join(", ", Enum.GetNames<VulnerabilityStatus>())}",
                });
            }

            try
            {
                var updated = await reporter.UpdateVulnerabilityStatusAsync(id, newStatus, body.Notes, ct);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("UpdateVulnerabilityStatus");

        // GET /api/security/events — security-relevant audit events.
        // Эвристика: всё, что логируется через IAuditService, попадает в эту ленту.
        // UI не делает серверной фильтрации, потому что вся фильтрация уже
        // отражена в `from/to/limit`.
        group.MapGet("/events", async (
            ISecurityAuditExporter exporter,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 100,
            CancellationToken ct = default) =>
        {
            var fromUtc = from ?? DateTime.UtcNow.AddDays(-7);
            var toUtc = to ?? DateTime.UtcNow;

            var export = await exporter.ExportAuditReportAsync(fromUtc, toUtc, AuditExportFormat.Json, ct);

            // Server-side cap before serialisation — последние N событий.
            var events = export.Events
                .OrderByDescending(e => e.Timestamp)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToList();

            return Results.Ok(new
            {
                from = export.From,
                to = export.To,
                total = export.TotalEvents,
                returned = events.Count,
                events,
                metrics = export.Metrics,
            });
        }).WithName("SecurityEvents");

        // GET /api/security/compliance/{standard} — compliance report.
        group.MapGet("/compliance/{standard}", async (
            string standard,
            ISecurityAuditExporter exporter,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<ComplianceStandard>(standard, true, out var std))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown standard: {standard}. Valid: {string.Join(", ", Enum.GetNames<ComplianceStandard>())}",
                });
            }

            var report = await exporter.ExportComplianceReportAsync(std, ct);
            return Results.Ok(report);
        }).WithName("SecurityCompliance");
    }
}

public sealed class UpdateVulnerabilityStatusRequest
{
    public string Status { get; set; } = "";
    public string? Notes { get; set; }
}
