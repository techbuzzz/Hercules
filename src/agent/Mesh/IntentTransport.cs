using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Hercules.Mesh;

/// <summary>
///     Транспорт inter-agent intent'ов. Отправляет IntentEnvelope на endpoint peer-агента
///     и получает IntentResponse. Поддерживает HTTP transport (по умолчанию) и bus adapter.
///     Использует IHttpClientFactory для корректного управления жизненным циклом HttpClient
///     (connection pooling, DNS refresh, timeout).
///     Спецификация: docs/ROADMAP-RU.md Phase 3 #15.
/// </summary>
public sealed class IntentTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly CapabilityRegistry _registry;
    private readonly bool _weOwnClient;

    /// <summary>
    ///     Создать транспорт с собственным HttpClient (для CLI-режима без DI).
    /// </summary>
    public IntentTransport(CapabilityRegistry registry, int defaultTimeoutMs = 30_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(defaultTimeoutMs)
        };
        DefaultTimeoutMs = defaultTimeoutMs;
        _weOwnClient = true;
    }

    /// <summary>
    ///     Создать транспорт с HttpClient из IHttpClientFactory (рекомендуется для Web API).
    /// </summary>
    public IntentTransport(CapabilityRegistry registry, HttpClient httpClient, int defaultTimeoutMs = 30_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        DefaultTimeoutMs = defaultTimeoutMs;
        _weOwnClient = false;
    }

    /// <summary>Таймаут по умолчанию для inter-agent вызовов (мс).</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    public void Dispose()
    {
        if (_weOwnClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    ///     Регистрация IntentTransport в DI с IHttpClientFactory.
    /// </summary>
    public static void RegisterWithHttpClient(IServiceCollection services, int defaultTimeoutMs = 30_000)
    {
        services.AddHttpClient<IntentTransport>(client => { client.Timeout = TimeSpan.FromMilliseconds(defaultTimeoutMs); });
    }

    /// <summary>
    ///     Отправить intent указанному peer-агенту по его agentId.
    ///     Endpoint и auth берутся из CapabilityRegistry.
    /// </summary>
    public async Task<IntentResponse> SendToAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAgentId);
        ArgumentNullException.ThrowIfNull(envelope);

        AgentManifest peerManifest = _registry.Get(targetAgentId) ?? throw new InvalidOperationException($"Peer-агент '{targetAgentId}' не найден в реестре.");

        if (string.IsNullOrWhiteSpace(peerManifest.Endpoint))
        {
            return IntentResponse.Failed(envelope.RequestId, targetAgentId,
                "Peer endpoint не сконфигурирован.", envelope.TraceId);
        }

        var intentUrl = peerManifest.Endpoint.TrimEnd('/') + "/api/mesh/intent";

        // Calculate timeout from deadline or use default
        var deadline = envelope.GetDeadlineOrDefault(DefaultTimeoutMs);
        var remainingMs = (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
        var timeoutMs = remainingMs > 0 ? remainingMs : DefaultTimeoutMs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, intentUrl)
            {
                Content = new StringContent(envelope.ToJson(), Encoding.UTF8, "application/json")
            };

            // Peer authentication
            if (peerManifest.Auth.Type == "apikey" && !string.IsNullOrEmpty(peerManifest.Auth.Header))
            {
                httpReq.Headers.TryAddWithoutValidation(peerManifest.Auth.Header, "mesh-key");
            }

            // Forward auth context from delegation envelope
            if (envelope.Auth is not null)
            {
                if (!string.IsNullOrEmpty(envelope.Auth.Token))
                {
                    httpReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {envelope.Auth.Token}");
                }

                if (envelope.Auth.DelegationDepth > 0)
                {
                    httpReq.Headers.TryAddWithoutValidation("X-Delegation-Depth", envelope.Auth.DelegationDepth.ToString());
                }

                if (!string.IsNullOrEmpty(envelope.Auth.RootRequestId))
                {
                    httpReq.Headers.TryAddWithoutValidation("X-Root-Request-Id", envelope.Auth.RootRequestId);
                }
            }

            // Forward idempotency key
            if (!string.IsNullOrEmpty(envelope.IdempotencyKey))
            {
                httpReq.Headers.TryAddWithoutValidation("X-Idempotency-Key", envelope.IdempotencyKey);
            }

            using HttpResponseMessage resp = await _http.SendAsync(httpReq, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);

            if (!resp.IsSuccessStatusCode)
            {
                return IntentResponse.Failed(envelope.RequestId, targetAgentId,
                    $"HTTP {(int)resp.StatusCode}: {body}", envelope.TraceId);
            }

            return IntentResponse.FromJson(body) ??
                   IntentResponse.Failed(envelope.RequestId, targetAgentId,
                       $"Невалидный JSON в ответе: {body}", envelope.TraceId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return IntentResponse.TimedOut(envelope.RequestId, targetAgentId, envelope.TraceId);
        }
        catch (Exception ex)
        {
            return IntentResponse.Failed(envelope.RequestId, targetAgentId,
                $"{ex.GetType().Name}: {ex.Message}", envelope.TraceId);
        }
    }
}
