using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Hercules.Mesh;

/// <summary>
///     Транспорт inter-agent intent'ов. Отправляет IntentEnvelope на endpoint peer-агента
///     и получает IntentResponse. Поддерживает HTTP transport (по умолчанию) и bus adapter.
///     Спецификация: docs/ROADMAP-RU.md Phase 3 #15.
/// </summary>
public sealed class IntentTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly CapabilityRegistry _registry;

    /// <summary>Таймаут по умолчанию для inter-agent вызовов (мс).</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    public IntentTransport(CapabilityRegistry registry, int defaultTimeoutMs = 30_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = new HttpClient();
        DefaultTimeoutMs = defaultTimeoutMs;
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>
    ///     Отправить intent указанному peer-агенту по его agentId.
    ///     Endpoint и auth берутся из CapabilityRegistry.
    /// </summary>
    public async Task<IntentResponse> SendToAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAgentId);
        ArgumentNullException.ThrowIfNull(envelope);

        var peerManifest = _registry.Get(targetAgentId)
            ?? throw new InvalidOperationException($"Peer-агент '{targetAgentId}' не найден в реестре.");

        if (string.IsNullOrWhiteSpace(peerManifest.Endpoint))
        {
            return IntentResponse.Failed(envelope.RequestId, targetAgentId,
                "Peer endpoint не сконфигурирован.", envelope.TraceId);
        }

        var intentUrl = peerManifest.Endpoint.TrimEnd('/') + "/api/mesh/intent";
        var timeoutMs = envelope.TimeoutMs > 0 ? envelope.TimeoutMs : DefaultTimeoutMs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, intentUrl)
            {
                Content = new StringContent(envelope.ToJson(), Encoding.UTF8, "application/json"),
            };

            // Auth header (еслиapikey)
            if (peerManifest.Auth.Type == "apikey" && !string.IsNullOrEmpty(peerManifest.Auth.Header))
            {
                httpReq.Headers.TryAddWithoutValidation(peerManifest.Auth.Header, "mesh-key");
            }

            using var resp = await _http.SendAsync(httpReq, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);

            if (!resp.IsSuccessStatusCode)
            {
                return IntentResponse.Failed(envelope.RequestId, targetAgentId,
                    $"HTTP {(int)resp.StatusCode}: {body}", envelope.TraceId);
            }

            return IntentResponse.FromJson(body)
                ?? IntentResponse.Failed(envelope.RequestId, targetAgentId,
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