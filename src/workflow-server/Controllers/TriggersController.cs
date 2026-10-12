using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Hercules.WorkflowServer.Config;

namespace Hercules.WorkflowServer.Controllers;

/// <summary>
///     Triggers для workflow-server (task_104, ADR-0008).
/// </summary>
/// <remarks>
///     R8/R10/R17 — the previous MVP accepted ANY non-empty token on a public,
///     authentication-exempt endpoint and appended the payload to a
///     <see cref="ConcurrentQueue{T}"/> whose advertised capacity
///     (<c>WebhookEventRingCapacity</c>) was never enforced. The queue therefore grew
///     without bound under unauthenticated load. The body was also read with a blocking
///     <c>StreamReader.ReadToEnd()</c> inside a synchronous delegate that then
///     <em>disposed</em> <c>HttpRequest.Body</c>, blocking a thread-pool thread and
///     breaking the framework's body pooling.
///     <para>
///     Now: an explicit token allow-list (empty ⇒ fail-closed), a genuinely bounded ring,
///     a hard request-body cap, and an awaited body read that never disposes
///     <c>HttpRequest.Body</c>.
///     </para>
/// </remarks>
public static class TriggersController
{
    /// <summary>Default ring capacity, used when <see cref="WebhookSection.EventBufferCapacity"/> is unset.</summary>
    public const int WebhookEventRingCapacity = 100;

    public static void MapTriggers(this IEndpointRouteBuilder app)
    {
        // POST /api/workflows/triggers/webhook/{token}
        app.MapPost("/api/workflows/triggers/webhook/{token}", async (
            string token,
            HttpContext ctx,
            WorkflowServerConfig cfg,
            CancellationToken ct) =>
        {
            var webhooks = cfg.Webhooks;

            // R10: fail closed. An empty allow-list disables the endpoint entirely rather
            // than accepting every caller, which is what the MVP did.
            if (webhooks.AllowedTokens.Count == 0)
            {
                return Results.Problem(
                    title: "Webhook disabled",
                    detail: "No webhook tokens are configured. Set WorkflowServer:Webhooks:AllowedTokens.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(token) || !TokenMatches(webhooks.AllowedTokens, token))
            {
                return Results.Problem(
                    title: "Unknown webhook token",
                    detail: "The supplied webhook token is not allowed.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // R17: cap the body BEFORE reading it, and read asynchronously. The previous
            // `new StreamReader(ctx.Request.Body)` + ReadToEnd() blocked a pool thread and
            // the `using` disposed the framework-owned request body stream.
            var maxBytes = webhooks.MaxRequestBodyBytes > 0 ? webhooks.MaxRequestBodyBytes : 256 * 1024;
            if (ctx.Request.ContentLength is { } declared && declared > maxBytes)
            {
                return Results.Problem(
                    title: "Payload too large",
                    detail: $"Webhook body exceeds the {maxBytes} byte limit.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            string body;
            try
            {
                body = await ReadBoundedAsync(ctx.Request.Body, maxBytes, ct).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return Results.Problem(
                    title: "Payload too large",
                    detail: $"Webhook body exceeds the {maxBytes} byte limit.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var evt = new WebhookEvent(
                Token: MaskToken(token),
                ReceivedAt: DateTimeOffset.UtcNow,
                Payload: body);

            // R8: enforce the ring capacity for real. Enqueue drops the oldest event once
            // capacity is reached, so memory stays bounded regardless of caller volume.
            EnqueueBounded(evt, webhooks.EventBufferCapacity);

            return Results.Accepted(value: new
            {
                accepted = true,
                receivedAt = evt.ReceivedAt,
            });
        }).WithName("WebhookTrigger")
          .AllowAnonymous(); // token in the URL authorises the request, not clientId/secret

        // GET /api/workflows/triggers/cron — list cron triggers (stub)
        app.MapGet("/api/workflows/triggers/cron", () =>
        {
            // R35: RFC 7807 problem+json instead of a 501 carrying a mixed error/data body.
            return Results.Problem(
                title: "Cron triggers not implemented",
                detail: "Cron triggers are a task_104 follow-up and are not available yet.",
                statusCode: StatusCodes.Status501NotImplemented);
        }).WithName("ListCronTriggers");
    }

    /// <summary>
    ///     Constant-time membership check. Compares against every candidate without an
    ///     early exit so the response time does not leak which token matched or how far a
    ///     guess got.
    /// </summary>
    private static bool TokenMatches(List<string> allowed, string candidate)
    {
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var matched = 0;

        foreach (var entry in allowed)
        {
            var entryBytes = Encoding.UTF8.GetBytes(entry ?? string.Empty);
            // FixedTimeEquals returns false (rather than throwing) on a length mismatch.
            if (CryptographicOperations.FixedTimeEquals(candidateBytes, entryBytes))
            {
                matched = 1;
            }
        }

        return matched == 1;
    }

    /// <summary>Never echo a full webhook token back to the caller or into the buffer.</summary>
    private static string MaskToken(string token) =>
        token.Length <= 4 ? "****" : token[..4] + new string('*', Math.Min(12, token.Length - 4));

    private static async Task<string> ReadBoundedAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var reader = new StreamReader(
            body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true, // R17: never dispose HttpRequest.Body — the framework owns it
            bufferSize: 4096);

        var buffer = new char[4096];
        var total = 0;
        var sb = new StringBuilder();

        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Webhook body exceeds the configured limit.");
            }

            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------------
    // Bounded event buffer (R8)
    // ------------------------------------------------------------------------

    private static readonly ConcurrentQueue<WebhookEvent> WebhookEvents = new();

    /// <summary>
    ///     Appends an event, evicting the oldest entries so the queue never exceeds
    ///     <paramref name="capacity"/>. Safe under concurrency: every queue mutation is
    ///     serialised on <see cref="EvictionGate"/>.
    /// </summary>
    private static void EnqueueBounded(WebhookEvent evt, int capacity)
    {
        var cap = capacity > 0 ? capacity : WebhookEventRingCapacity;

        lock (EvictionGate)
        {
            WebhookEvents.Enqueue(evt);
            while (WebhookEvents.Count > cap && WebhookEvents.TryDequeue(out _))
            {
                // drop oldest
            }
        }
    }

    private static readonly object EvictionGate = new();

    /// <summary>Одно webhook-событие (для отладки и тестов).</summary>
    public sealed record WebhookEvent(string Token, DateTimeOffset ReceivedAt, string Payload);

    /// <summary>Снимок кольцевого буфера (только для тестов и диагностики).</summary>
    public static IReadOnlyCollection<WebhookEvent> SnapshotEvents() => WebhookEvents.ToArray();

    /// <summary>Очистка буфера (тесты).</summary>
    internal static void ClearEvents()
    {
        lock (EvictionGate)
        {
            while (WebhookEvents.TryDequeue(out _)) { }
        }
    }
}