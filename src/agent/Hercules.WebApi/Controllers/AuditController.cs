using System.Globalization;
using Hercules.Audit;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинты аудит-лога (task_003 / task_014).
///     task_014 расширяет: search с фильтрами, export CSV, enriched fields.
/// </summary>
public static class AuditController
{
    public static void MapAudit(this IEndpointRouteBuilder app)
    {
        // GET /api/audit — последние N записей (расширенные поля task_014)
        app.MapGet("/api/audit", async (IAuditService audit, int limit = 100, CancellationToken ct = default) =>
        {
            var entries = await audit.QueryAsync(limit: limit, ct: ct);
            return Results.Ok(new
            {
                count = entries.Count,
                entries = entries.Select(MapEntry)
            });
        }).WithName("AuditLog");

        // GET /api/audit/search — поиск с фильтрами (task_014)
        app.MapGet("/api/audit/search", async (
            IAuditService audit,
            string? actor = null,
            string? action = null,
            string? sessionId = null,
            string? toolName = null,
            string? result = null,
            string? from = null,
            string? to = null,
            int limit = 100,
            CancellationToken ct = default) =>
        {
            DateTime? fromDate = null, toDate = null;
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f))
            {
                fromDate = DateTime.SpecifyKind(f, DateTimeKind.Utc);
            }
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            {
                toDate = DateTime.SpecifyKind(t, DateTimeKind.Utc);
            }

            var entries = await audit.QueryAsync(
                actor: actor,
                action: action,
                sessionId: sessionId,
                toolName: toolName,
                result: result,
                from: fromDate,
                to: toDate,
                limit: limit,
                ct: ct);

            return Results.Ok(new
            {
                filters = new { actor, action, sessionId, toolName, result, from, to },
                count = entries.Count,
                entries = entries.Select(MapEntry)
            });
        }).WithName("AuditSearch");

        // GET /api/audit/export — экспорт в CSV (task_014, task_015: redaction)
        app.MapGet("/api/audit/export", async (
            IAuditService audit,
            ISecretMaskingService? masking,
            SecretsConfig? secretsConfig,
            string? actor = null,
            string? action = null,
            string? sessionId = null,
            int limit = 1000,
            CancellationToken ct = default) =>
        {
            var entries = await audit.QueryAsync(
                actor: actor,
                action: action,
                sessionId: sessionId,
                limit: limit,
                ct: ct);

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("id,actor,action,target,session_id,request_id,tool_name,policy_decision,permission_used,result,payload_hash,created_at");
            foreach (var e in entries)
            {
                var details = e.Details is not null && masking is not null && secretsConfig?.RedactInExports != false
                    ? masking.MaskSecrets(e.Details)
                    : e.Details;
                var policyDecision = e.PolicyDecision is not null && masking is not null && secretsConfig?.RedactInExports != false
                    ? masking.MaskSecrets(e.PolicyDecision)
                    : e.PolicyDecision;
                var permUsed = e.PermissionUsed is not null && masking is not null && secretsConfig?.RedactInExports != false
                    ? masking.MaskSecrets(e.PermissionUsed)
                    : e.PermissionUsed;

                csv.AppendLine($"{e.Id},{EscapeCsv(e.Actor)},{EscapeCsv(e.Action)},{EscapeCsv(e.Target)},{EscapeCsv(e.SessionId)},{EscapeCsv(e.RequestId)},{EscapeCsv(e.ToolName)},{EscapeCsv(policyDecision)},{EscapeCsv(permUsed)},{EscapeCsv(e.Result)},{EscapeCsv(e.PayloadHash)},{e.CreatedAt:o}");
            }

            return Results.Text(csv.ToString(), "text/csv");
        }).WithName("AuditExport");

        // GET /api/audit/{target} — записи по target
        app.MapGet("/api/audit/{target}", async (IAuditService audit, string target, int limit = 50, CancellationToken ct = default) =>
        {
            var entries = await audit.QueryAsync(target: target, limit: limit, ct: ct);
            return Results.Ok(new
            {
                target,
                count = entries.Count,
                entries = entries.Select(MapEntry)
            });
        }).WithName("AuditLogByTarget");
    }

    private static object MapEntry(AuditLogEntry e) => new
    {
        e.Id,
        e.Actor,
        e.Action,
        e.Target,
        e.Details,
        e.SessionId,
        e.RequestId,
        e.ToolName,
        e.PolicyDecision,
        e.PermissionUsed,
        e.Result,
        e.PayloadHash,
        e.CreatedAt
    };

    private static string EscapeCsv(string? value) =>
        string.IsNullOrEmpty(value) ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
}
