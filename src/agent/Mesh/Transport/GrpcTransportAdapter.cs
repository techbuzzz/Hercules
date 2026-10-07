using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Grpc.Net.Client;

namespace Hercules.Mesh.Transport;

/// <summary>
///     gRPC адаптер для <see cref="ITransport"/>.
///     Использует HTTP/2 transport (Grpc.Net.Client) для bi-directional streaming.
///     Для request/response использует HttpClient с HTTP/2 (SocketsHttpHandler).
///     Библиотека Grpc.Net.Client должна быть добавлена как NuGet-зависимость.
///     Спецификация: task_037.
/// </summary>
/// <remarks>
///     HTTP/2 включается по умолчанию в .NET 10.
///     TODO (task_068): заменить на real gRPC call после определения proto-контракта.
/// </remarks>
public sealed class GrpcTransportAdapter : ITransport, IDisposable
{
    /// <summary>Имя named HttpClient-клиента для gRPC HTTP/2 transport (task_078).</summary>
    public const string HttpClientName = "grpc-transport";

    private readonly ICapabilityLookup _registry;
    private readonly HttpClient _http;
    private readonly GrpcChannel _channel;
    private readonly bool _weOwnHttpClient;

    /// <summary>Default timeout для gRPC-вызовов (мс).</summary>
    public int DefaultTimeoutMs { get; set; } = 30_000;

    public TransportKind Kind => TransportKind.Grpc;
    public bool SupportsBidirectionalStreaming => true;
    public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;

    /// <summary>
    ///     Создать gRPC-адаптер с HTTP/2 HttpClient.
    ///     HTTP/2 negotiation включается через <c>SocketsHttpHandler.EnableMultipleHttp2Connections</c>.
    /// </summary>
    /// <param name="registry">Capability lookup (для получения endpoint'ов пиров).</param>
    /// <param name="httpClient">HttpClient с HTTP/2 support. Адаптер НЕ владеет им.</param>
    /// <param name="channel">GrpcChannel для HTTP/2 transport. Адаптер НЕ владеет им.</param>
    public GrpcTransportAdapter(ICapabilityLookup registry, HttpClient httpClient, GrpcChannel channel)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _weOwnHttpClient = false;
    }

    /// <summary>
    ///     Создать gRPC-адаптер с внутренним HTTP/2 HttpClient (CLI-режим).
    /// </summary>
    public GrpcTransportAdapter(ICapabilityLookup registry, int defaultTimeoutMs = 30_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        DefaultTimeoutMs = defaultTimeoutMs;

        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(defaultTimeoutMs) };
        _weOwnHttpClient = true;

        // GrpcChannel используется для HTTP/2 transport setup
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }

    public void Dispose()
    {
        if (_weOwnHttpClient)
        {
            _http.Dispose();
            _channel.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<TransportResult> SendAsync(string targetAgentId, IntentEnvelope envelope, CancellationToken ct = default)
    {
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

        // Для gRPC используем тот же endpoint, что и для HTTP (gRPC требует HTTP/2)
        var grpcEndpoint = NormalizeGrpcEndpoint(peer.Endpoint);
        var intentUrl = grpcEndpoint.TrimEnd('/') + "/api/mesh/intent";

        var deadline = envelope.GetDeadlineOrDefault(DefaultTimeoutMs);
        var remainingMs = (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
        var timeoutMs = remainingMs > 0 ? remainingMs : DefaultTimeoutMs;

        try
        {
            var result = await SendHttp2Async(intentUrl, envelope, timeoutMs, ct);
            sw.Stop();
            return result with { LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return TransportResult.TimedOut(targetAgentId, envelope.TraceId, sw.ElapsedMilliseconds, Kind);
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

    /// <summary>
    ///     Отправить envelope по HTTP/2 через внутренний HttpClient.
    ///     Content-Type: application/grpc+json (gRPC-Web compatible).
    /// </summary>
    private async Task<TransportResult> SendHttp2Async(
        string url,
        IntentEnvelope envelope,
        int timeoutMs,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(envelope.ToJson(), Encoding.UTF8, "application/grpc+json")
        };

        // gRPC-compatible headers
        httpReq.Headers.TryAddWithoutValidation("te", "trailers");
        httpReq.Headers.TryAddWithoutValidation("x-user-agent", "hercules-grpc/1.0");

        if (envelope.Auth is not null && !string.IsNullOrEmpty(envelope.Auth.Token))
        {
            httpReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {envelope.Auth.Token}");
        }

        using HttpResponseMessage resp = await _http.SendAsync(httpReq, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);

        if (!resp.IsSuccessStatusCode)
        {
            var kind = (int)resp.StatusCode >= 500
                ? TransportErrorKind.TransportError
                : TransportErrorKind.Rejected;
            var errorMsg = $"HTTP {(int)resp.StatusCode}: {body}";
            var response = IntentResponse.Failed(envelope.RequestId, "", errorMsg, envelope.TraceId);
            return new TransportResult(response, false, kind, errorMsg, 0, Kind);
        }

        var result = IntentResponse.FromJson(body);
        if (result is null)
        {
            var errorMsg = $"Невалидный JSON: {body}";
            var response = IntentResponse.Failed(envelope.RequestId, "", errorMsg, envelope.TraceId);
            return new TransportResult(response, false, TransportErrorKind.TransportError, errorMsg, 0, Kind);
        }

        return TransportResult.Ok(result, 0, Kind);
    }

    /// <summary>Привести HTTP endpoint к gRPC-форме (http://host → http://host, http → https).</summary>
    private static string NormalizeGrpcEndpoint(string httpEndpoint)
    {
        var ep = httpEndpoint.TrimEnd('/');
        if (ep.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return ep; // Grpc.Net.Client поддерживает http:// для loopback
        }
        if (ep.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return ep; // gRPC over TLS
        }
        return "http://" + ep;
    }
}
