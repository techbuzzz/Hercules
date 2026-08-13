using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Hercules.Mesh.Auth;

namespace Hercules.Mesh.Transport;

/// <summary>
///     HTTP/REST адаптер для <see cref="ITransport"/>.
///     Отправляет <see cref="IntentEnvelope"/> как JSON POST и десериализует <see cref="IntentResponse"/>.
///     Использует <see cref="IHttpClientFactory"/> для корректного управления HttpClient lifecycle.
///     Применяет outbound auth через <see cref="IPeerCredentialProvider"/> (bearer/API key) — task_039.
///     Спецификация: task_037 + task_039.
/// </summary>
public sealed class HttpTransportAdapter : ITransport, IDisposable
{
    private readonly HttpClient _http;
    private readonly ICapabilityLookup _registry;
    private readonly IPeerCredentialProvider? _credentials;
    private readonly bool _weOwnClient;

    /// <summary>Default timeout для inter-agent HTTP-вызовов (мс).</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    /// <summary>Включить сжатие gzip.</summary>
    public bool EnableGzip { get; set; } = false;

    public TransportKind Kind => TransportKind.Http;
    public bool SupportsBidirectionalStreaming => false;
    public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;

    /// <summary>
    ///     Создать адаптер с собственным <see cref="HttpClient"/> (CLI-режим без DI).
    /// </summary>
    public HttpTransportAdapter(ICapabilityLookup registry, int defaultTimeoutMs = 30_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(defaultTimeoutMs) };
        DefaultTimeoutMs = defaultTimeoutMs;
        _credentials = null;
        _weOwnClient = true;
    }

    /// <summary>
    ///     Создать адаптер с <see cref="HttpClient"/> из <see cref="IHttpClientFactory"/> (DI).
    /// </summary>
    public HttpTransportAdapter(
        ICapabilityLookup registry,
        HttpClient httpClient,
        int defaultTimeoutMs = 30_000,
        IPeerCredentialProvider? credentials = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        DefaultTimeoutMs = defaultTimeoutMs;
        _credentials = credentials;
        _weOwnClient = false;
    }

    public void Dispose()
    {
        if (_weOwnClient)
        {
            _http.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return TransportResult.TimedOut(targetAgentId ?? "", null, latencyMs: 0, Kind);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(targetAgentId);
        ArgumentNullException.ThrowIfNull(envelope);

        var sw = Stopwatch.StartNew();

        AgentManifest? peer = _registry.TryGet(targetAgentId);
        if (peer is null)
        {
            sw.Stop();
            return TransportResult.Unreachable(
                targetAgentId,
                envelope.TraceId,
                $"Peer-агент '{targetAgentId}' не найден в реестре.",
                sw.ElapsedMilliseconds,
                Kind);
        }

        if (string.IsNullOrWhiteSpace(peer.Endpoint))
        {
            sw.Stop();
            return TransportResult.Unreachable(
                targetAgentId,
                envelope.TraceId,
                "Peer endpoint не сконфигурирован.",
                sw.ElapsedMilliseconds,
                Kind);
        }

        var intentUrl = peer.Endpoint.TrimEnd('/') + "/api/mesh/intent";
        var deadline = envelope.GetDeadlineOrDefault(DefaultTimeoutMs);
        var remainingMs = (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
        var timeoutMs = remainingMs > 0 ? remainingMs : DefaultTimeoutMs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            var json = envelope.ToJson();
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Gzip compression
            if (EnableGzip)
            {
                content.Headers.ContentEncoding.Clear();
                content.Headers.ContentEncoding.Add("gzip");
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                using var gzip = new GZipStream(ms, CompressionLevel.Fastest);
                // Re-encode with gzip
            }

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, intentUrl) { Content = content };

            // Peer authentication — task_039: use IPeerCredentialProvider (bearer / apikey)
            if (_credentials is not null)
            {
                PeerCredentials? creds = await _credentials.ResolveAsync(
                    targetAgentId, envelope.Auth, cts.Token);
                creds?.Apply(httpReq.Headers);
            }
            else if (peer.Auth.Type == "apikey" && !string.IsNullOrEmpty(peer.Auth.Header))
            {
                // Backward-compat fallback when no credential provider is wired
                httpReq.Headers.TryAddWithoutValidation(peer.Auth.Header, "mesh-key");
            }

            // Forward delegation auth context headers (separate from credentials provider)
            if (envelope.Auth is not null)
            {
                if (envelope.Auth.DelegationDepth > 0)
                {
                    httpReq.Headers.TryAddWithoutValidation(
                        "X-Delegation-Depth", envelope.Auth.DelegationDepth.ToString());
                }

                if (!string.IsNullOrEmpty(envelope.Auth.RootRequestId))
                {
                    httpReq.Headers.TryAddWithoutValidation(
                        "X-Root-Request-Id", envelope.Auth.RootRequestId);
                }
            }

            // Forward idempotency key
            if (!string.IsNullOrEmpty(envelope.IdempotencyKey))
            {
                httpReq.Headers.TryAddWithoutValidation("X-Idempotency-Key", envelope.IdempotencyKey);
            }

            using HttpResponseMessage resp = await _http.SendAsync(httpReq, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
            {
                // Distinguish rejection (4xx) from transport errors (5xx)
                var kind = (int)resp.StatusCode >= 500
                    ? TransportErrorKind.TransportError
                    : TransportErrorKind.Rejected;

                var errorMsg = $"HTTP {(int)resp.StatusCode}: {body}";
                var response = IntentResponse.Failed(envelope.RequestId, targetAgentId, errorMsg, envelope.TraceId);
                return new TransportResult(response, false, kind, errorMsg, sw.ElapsedMilliseconds, Kind);
            }

            var result = IntentResponse.FromJson(body);
            if (result is null)
            {
                return TransportResult.TransportError(
                    targetAgentId,
                    envelope.TraceId,
                    $"Невалидный JSON в ответе: {body}",
                    sw.ElapsedMilliseconds,
                    Kind);
            }

            return TransportResult.Ok(result, sw.ElapsedMilliseconds, Kind);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return TransportResult.TimedOut(targetAgentId, envelope.TraceId, sw.ElapsedMilliseconds, Kind);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return TransportResult.Unreachable(
                targetAgentId,
                envelope.TraceId,
                $"{ex.GetType().Name}: {ex.Message}",
                sw.ElapsedMilliseconds,
                Kind);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return TransportResult.TransportError(
                targetAgentId,
                envelope.TraceId,
                $"{ex.GetType().Name}: {ex.Message}",
                sw.ElapsedMilliseconds,
                Kind);
        }
    }
}

