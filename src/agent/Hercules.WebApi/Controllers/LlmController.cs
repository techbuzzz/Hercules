using Hercules.Config;
using Hercules.LLM;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     LLM provider health, capability and configuration endpoints.
/// </summary>
public static class LlmController
{
    public static void MapLlm(this IEndpointRouteBuilder app)
    {
        // GET /api/llm/health — общий health-check по всем провайдерам.
        app.MapGet("/api/llm/health", async (ProviderHealthChecker healthChecker, CancellationToken ct) =>
        {
            var results = await healthChecker.CheckAllAsync(ct);
            return Results.Ok(results);
        }).WithName("LlmHealthAll");

        // GET /api/llm/health/{provider} — health-check конкретного провайдера.
        app.MapGet("/api/llm/health/{provider}", async (string provider, ProviderHealthChecker healthChecker, CancellationToken ct) =>
        {
            var result = await healthChecker.CheckAsync(provider, ct);
            return Results.Ok(result);
        }).WithName("LlmHealthByProvider");

        // GET /api/llm/capabilities — общие capabilities по всем провайдерам.
        app.MapGet("/api/llm/capabilities", async (ProviderCapabilityDetector capabilityDetector, CancellationToken ct) =>
        {
            var results = await capabilityDetector.DetectAllAsync(ct);
            return Results.Ok(results);
        }).WithName("LlmCapabilitiesAll");

        // GET /api/llm/capabilities/{provider} — capabilities конкретного провайдера.
        app.MapGet("/api/llm/capabilities/{provider}", async (string provider, ProviderCapabilityDetector capabilityDetector, CancellationToken ct) =>
        {
            var result = await capabilityDetector.DetectAsync(provider, ct);
            return Results.Ok(result);
        }).WithName("LlmCapabilitiesByProvider");

        // GET /api/llm/config — публичный конфиг (без секретов).
        app.MapGet("/api/llm/config", (LlmConfig cfg) =>
        {
            return Results.Ok(new LlmConfigDto
            {
                Provider = cfg.Provider,
                Fallback = cfg.Fallback,
                YandexGptModel = cfg.YandexGpt.Model,
                OllamaCloudModel = cfg.OllamaCloud.Model,
                OllamaLocalModel = cfg.OllamaLocal.Model,
                OpenAICompatibleEndpoint = cfg.OpenAICompatible.Endpoint,
                OpenAICompatibleModel = cfg.OpenAICompatible.Model,
                OpenAICompatibleDisplayName = cfg.OpenAICompatible.DisplayName
            });
        }).WithName("LlmConfig");
    }
}

/// <summary>Public LLM config DTO (no secrets).</summary>
public sealed record LlmConfigDto
{
    public string Provider { get; init; } = "";
    public List<string> Fallback { get; init; } = [];
    public string YandexGptModel { get; init; } = "";
    public string OllamaCloudModel { get; init; } = "";
    public string OllamaLocalModel { get; init; } = "";
    public string OpenAICompatibleEndpoint { get; init; } = "";
    public string OpenAICompatibleModel { get; init; } = "";
    public string OpenAICompatibleDisplayName { get; init; } = "";
}
