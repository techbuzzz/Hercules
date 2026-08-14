using System.Text.Json;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.LLM.Providers;

/// <summary>
///     Клиент для LM Studio с дополнительным health-детектированием.
///     Использует тот же OpenAI-совместимый интерфейс, что и OpenAICompatibleClient,
///     но добавляет LM Studio-specific endpoints для model detection.
/// </summary>
public sealed class LMStudioClient : ILLMClient
{
    /// <summary>Имя named HttpClient-клиента для LM Studio detection (task_078).</summary>
    public const string HttpClientName = "lm-studio-info";

    private readonly OpenAICompatibleConfig _cfg;
    private readonly ILLMClient _delegate;
    private readonly ILogger<LMStudioClient>? _logger;
    private readonly IHttpClientFactory? _httpFactory;

    public LMStudioClient(OpenAICompatibleConfig cfg, ILogger<LMStudioClient>? logger = null)
        : this(cfg, logger, httpFactory: null)
    {
    }

    /// <summary>DI-friendly конструктор (task_078): использует named-клиент "lm-studio-info".</summary>
    public LMStudioClient(
        OpenAICompatibleConfig cfg,
        ILogger<LMStudioClient>? logger,
        IHttpClientFactory? httpFactory)
    {
        _cfg = cfg;
        _delegate = new OpenAICompatibleClient(cfg, "lmstudio");
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public string ProviderName => "lmstudio";
    public string ModelName => _cfg.Model;

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => _delegate.CompleteAsync(role, messages, ct);

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => _delegate.CompleteAsync(messages, ct);

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => _delegate.StreamAsync(role, messages, ct);

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => _delegate.StreamAsync(messages, ct);

    /// <summary>
    ///     LM Studio `/api/info` endpoint — определяет активную модель и параметры сервера.
    ///     Возвращает model, rollback, ctx, stopping_strings, host.
    /// </summary>
    public async Task<LMStudioInfo?> GetServerInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var baseUrl = _cfg.Endpoint.TrimEnd('/');
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^3];
            }

            using var client = ResolveClient();
            var response = await client.GetStringAsync($"{baseUrl}/api/info", ct);
            return JsonSerializer.Deserialize<LMStudioInfo>(response, LMStudioInfoOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LM Studio /api/info failed at {Endpoint}", _cfg.Endpoint);
            return null;
        }
    }

    private HttpClient ResolveClient()
    {
        if (_httpFactory is not null)
        {
            return _httpFactory.CreateClient(HttpClientName);
        }
        return new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    private static readonly JsonSerializerOptions LMStudioInfoOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

/// <summary>Ответ LM Studio `/api/info`.</summary>
public sealed record LMStudioInfo
{
    public string? Model { get; init; }
    public string? Rollback { get; init; }
    public int Ctx { get; init; }
    public List<string>? StoppingStrings { get; init; }
    public string? Host { get; init; }
}
