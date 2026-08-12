using Hercules.Config;
using Hercules.LLM;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Hercules.WebApi.Controllers;

/// <summary>
///     LLM provider health, capability and configuration endpoints.
/// </summary>
[ApiController]
public sealed class LlmController : ControllerBase
{
    private readonly LlmConfig _cfg;
    private readonly ProviderHealthChecker _healthChecker;
    private readonly ProviderCapabilityDetector _capabilityDetector;

    public LlmController(
        LlmConfig cfg,
        ProviderHealthChecker healthChecker,
        ProviderCapabilityDetector capabilityDetector)
    {
        _cfg = cfg;
        _healthChecker = healthChecker;
        _capabilityDetector = capabilityDetector;
    }

    [HttpGet("/api/llm/health")]
    public async Task<ActionResult<IReadOnlyList<ProviderHealthResult>>> GetHealth(CancellationToken ct = default)
    {
        var results = await _healthChecker.CheckAllAsync(ct);
        return Ok(results);
    }

    [HttpGet("/api/llm/health/{provider}")]
    public async Task<ActionResult<ProviderHealthResult>> GetHealthByProvider(string provider, CancellationToken ct = default)
    {
        var result = await _healthChecker.CheckAsync(provider, ct);
        return Ok(result);
    }

    [HttpGet("/api/llm/capabilities")]
    public async Task<ActionResult<IReadOnlyList<ProviderCapabilities>>> GetCapabilities(CancellationToken ct = default)
    {
        var results = await _capabilityDetector.DetectAllAsync(ct);
        return Ok(results);
    }

    [HttpGet("/api/llm/capabilities/{provider}")]
    public async Task<ActionResult<ProviderCapabilities>> GetCapabilitiesByProvider(string provider, CancellationToken ct = default)
    {
        var result = await _capabilityDetector.DetectAsync(provider, ct);
        return Ok(result);
    }

    [HttpGet("/api/llm/config")]
    public ActionResult<LlmConfigDto> GetConfig()
    {
        return Ok(new LlmConfigDto
        {
            Provider = _cfg.Provider,
            Fallback = _cfg.Fallback,
            YandexGptModel = _cfg.YandexGpt.Model,
            OllamaCloudModel = _cfg.OllamaCloud.Model,
            OllamaLocalModel = _cfg.OllamaLocal.Model,
            OpenAICompatibleEndpoint = _cfg.OpenAICompatible.Endpoint,
            OpenAICompatibleModel = _cfg.OpenAICompatible.Model,
            OpenAICompatibleDisplayName = _cfg.OpenAICompatible.DisplayName
        });
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

/// <summary>Extension method to map LLM endpoints.</summary>
public static class LlmControllerExtensions
{
    public static void MapLlm(this IEndpointRouteBuilder app)
    {
        app.MapControllers();
    }
}
