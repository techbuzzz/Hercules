using Hercules.Agent;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     Эндпоинт чата: POST /api/chat → AgentCore.HandleAsync() через WebApiAdapter.
///     Поддерживает optional <c>X-Session-Id</c> header для multi-tenant режима (task_075 H7).
///     Когда header отсутствует или пуст — используется process-default sessionId,
///     сохранён обратно-совместимый single-tenant путь.
/// </summary>
public static class ChatController
{
    public const string SessionHeader = "X-Session-Id";

    public static void MapChat(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
                HttpRequest http,
                ChatRequest req,
                WebApiAdapter adapter,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Message))
                {
                    return Results.BadRequest(new { error = "Поле 'message' не может быть пустым." });
                }

                // Per-request session id (task_075 H7): parallel requests must not share state.
                var sessionId = ResolveSessionId(http, adapter);
                var logger = loggerFactory.CreateLogger("ChatController");

                // First touch for this session — initialise memory + sessions store.
                EnsureSessionInitialised(adapter, sessionId, logger);

                var resp = await adapter.ChatAsync(req.Message, sessionId, ct);
                return Results.Ok(resp);
            })
            .WithName("Chat")
            .WithSummary("Отправить сообщение агенту и получить ответ");
    }

    private static string ResolveSessionId(HttpRequest http, WebApiAdapter adapter)
    {
        if (http.Headers.TryGetValue(SessionHeader, out var values))
        {
            var fromHeader = values.ToString();
            if (!string.IsNullOrWhiteSpace(fromHeader))
            {
                return fromHeader.Trim();
            }
        }
        return adapter.DefaultSessionId;
    }

    private static void EnsureSessionInitialised(WebApiAdapter adapter, string sessionId, ILogger logger)
    {
        if (string.Equals(sessionId, adapter.DefaultSessionId, StringComparison.Ordinal))
        {
            // Default session is initialised once at app startup (Program.cs).
            return;
        }

        try
        {
            adapter.EnsureSessionStarted(sessionId);
        }
        catch (Exception ex)
        {
            // Don't fail the request — agent will create the session on-demand via HandleAsync fallback.
            logger.LogWarning(ex, "EnsureSessionStarted failed for session {SessionId}", sessionId);
        }
    }
}
