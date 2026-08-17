using System.Collections.Concurrent;
using System.Text.Json;

namespace Hercules.WorkflowServer.Controllers;

/// <summary>
///     Triggers для workflow-server (task_104, ADR-0008).
///     MVP-реализация: webhook endpoint принимает любой token и пишет событие в in-memory ring buffer;
///     cron endpoint возвращает статический stub-список. Реальный cron-scheduler (Quartz/Hangfire) и
///     token-реестр — follow-up.
///     Webhook — публичный (token авторизует запрос), остальные — под ClientAuthMiddleware.
/// </summary>
public static class TriggersController
{
    /// <summary>Ёмкость in-memory ring buffer для записи последних webhook-событий (для отладки).</summary>
    public const int WebhookEventRingCapacity = 100;

    public static void MapTriggers(this IEndpointRouteBuilder app)
    {
        // POST /api/workflows/triggers/webhook/{token} — public, token авторизует запрос
        // MVP: принимаем любой непустой token, логируем payload, кладём событие в ring.
        // Реальный token-реестр + per-token startWorkflow — follow-up.
        app.MapPost("/api/workflows/triggers/webhook/{token}", (
            string token,
            HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.BadRequest(new { error = "Token is required." });
            }

            // Читаем body как raw JSON для логирования (без строгой схемы — триггеры должны
            // принимать произвольные внешние payloads).
            string body = "";
            using (var reader = new StreamReader(ctx.Request.Body))
            {
                body = reader.ReadToEnd();
            }

            var evt = new WebhookEvent(
                Token: token,
                ReceivedAt: DateTimeOffset.UtcNow,
                Payload: body);

            WebhookEvents.Enqueue(evt);

            return Results.Accepted(value: new
            {
                accepted = true,
                token,
                receivedAt = evt.ReceivedAt,
            });
        }).WithName("WebhookTrigger")
          .AllowAnonymous(); // webhook авторизуется по токену в URL, не по clientId/secret

        // GET /api/workflows/triggers/cron — list cron triggers (stub)
        app.MapGet("/api/workflows/triggers/cron", () =>
        {
            return Results.Json(
                new
                {
                    error = "Cron triggers not implemented yet (task_104 follow-up).",
                    triggers = Array.Empty<object>(),
                },
                statusCode: StatusCodes.Status501NotImplemented);
        }).WithName("ListCronTriggers");
    }

    /// <summary>Потокобезопасный in-memory ring buffer последних webhook-событий.</summary>
    private static readonly ConcurrentQueue<WebhookEvent> WebhookEvents = new();

    /// <summary>Один webhook-событие (для отладки и тестов).</summary>
    public sealed record WebhookEvent(string Token, DateTimeOffset ReceivedAt, string Payload);

    /// <summary>Снимок ring buffer'а (только для тестов и диагностики).</summary>
    public static IReadOnlyCollection<WebhookEvent> SnapshotEvents() => WebhookEvents.ToArray();
}
