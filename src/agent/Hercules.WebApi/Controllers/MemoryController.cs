using Hercules.Agent;

namespace Hercules.WebApi.Controllers;

/// <summary>
/// Эндпоинты памяти: профиль пользователя (чтение/обновление) и сброс памяти.
/// Делегируют в MemoryManager через WebApiAdapter.
/// </summary>
public static class MemoryController
{
    public static void MapMemory(this IEndpointRouteBuilder app)
    {
        // GET /api/memory/profile — получить профиль (markdown)
        app.MapGet("/api/memory/profile", (WebApiAdapter adapter) =>
            Results.Ok(new { content = adapter.GetProfile() }))
            .WithName("GetProfile");

        // PUT /api/memory/profile — обновить профиль
        app.MapPut("/api/memory/profile", (UpdateProfileRequest req, WebApiAdapter adapter) =>
        {
            adapter.UpdateProfile(req.Content ?? "");
            return Results.Ok(new { status = "ok", content = adapter.GetProfile() });
        }).WithName("UpdateProfile");

        // POST /api/memory/reset — сбросить память
        app.MapPost("/api/memory/reset", (WebApiAdapter adapter) =>
        {
            adapter.ResetMemory();
            return Results.Ok(new { status = "reset" });
        }).WithName("ResetMemory");
    }
}
