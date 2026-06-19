using Hercules.Agent;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// Эндпоинт чата: POST /api/chat → AgentCore.ProcessMessageAsync() через WebApiAdapter.
/// </summary>
public static class ChatController
{
    public static void MapChat(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (ChatRequest req, WebApiAdapter adapter, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new { error = "Поле 'message' не может быть пустым." });
            }

            var resp = await adapter.ChatAsync(req.Message, ct);
            return Results.Ok(resp);
        })
        .WithName("Chat")
        .WithSummary("Отправить сообщение агенту и получить ответ");
    }
}
