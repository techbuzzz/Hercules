using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HerculesBus.Core;

namespace HerculesBus.Http;

/// <summary>
///     Минимальный HTTP-сервер для HerculesBus на базе HttpListener (zero-deps).
///     POST /messages — отправить сообщение
///     GET  /channels — список каналов
///     POST /channels — создать канал
///     GET  /channels/{name}/recent?limit=N — последние N сообщений (JSON)
///     GET  /channels/{name}/stream — Server-Sent Events для real-time push
///     GET  /agents — список зарегистрированных агентов
///     POST /agents/register — зарегистрировать агента
///     POST /agents/{id}/heartbeat — обновить статус
///     GET  /healthz — liveness probe
///     V3.1: single-node, plain HTTP, token auth через X-Hercules-Token header.
///     V3.2: HTTPS + mTLS + WebSocket + Prometheus metrics.
/// </summary>
public sealed class HerculesBusHttpServer : IAsyncDisposable
{
    private readonly Bus _bus;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _validTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAsyncDisposable> _activeSubscriptions = new();
    private readonly object _subLock = new();
    private Task? _acceptLoop;
    private bool _started;

    public string BaseUrl => $"http://{_prefix}/";
    private readonly string _prefix;

    public HerculesBusHttpServer(Bus bus, string prefix = "http://localhost:9876/")
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        if (!prefix.EndsWith("/")) prefix += "/";
        _prefix = prefix;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
    }

    /// <summary>Зарегистрировать допустимые токены для аутентификации.</summary>
    public void AddToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _validTokens.Add(token);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;
        _started = true;
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), ct);
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => HandleRequestAsync(ctx, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;

            // Auth check (кроме /healthz)
            if (path != "/healthz" && !Authenticate(ctx))
            {
                await WriteJson(ctx, 401, new { error = "Unauthorized" });
                return;
            }

            switch ((method, path))
            {
                case ("GET", "/healthz"):
                    await WriteJson(ctx, 200, new { status = "ok", time = DateTimeOffset.UtcNow });
                    return;

                case ("GET", "/agents"):
                    var agents = await _bus.ListAgentsAsync(includeOffline: true, ct);
                    await WriteJson(ctx, 200, agents);
                    return;

                case ("POST", "/agents/register"):
                    var identity = await ReadJson<AgentIdentityDto>(ctx);
                    var result = await _bus.RegisterAsync(identity.ToIdentity(), ct);
                    await WriteJson(ctx, 200, result);
                    return;

                case ("GET", "/channels"):
                    var channels = await _bus.ListChannelsAsync(ct);
                    await WriteJson(ctx, 200, channels);
                    return;

                case ("POST", "/channels"):
                    var createDto = await ReadJson<CreateChannelDto>(ctx);
                    var ch = await _bus.EnsureChannelAsync(createDto.Name, createDto.Description ?? "", createDto.IsPrivate, createDto.CreatedBy ?? "system", ct);
                    await WriteJson(ctx, 200, ch);
                    return;

                case ("POST", "/messages"):
                    var msgDto = await ReadJson<AgentMessageDto>(ctx);
                    var msg = msgDto.ToMessage();
                    var saved = await _bus.SendAsync(msg, ct);
                    await WriteJson(ctx, 200, saved);
                    return;

                default:
                    // /channels/{name}/recent
                    // /channels/{name}/stream
                    if (path.StartsWith("/channels/", StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = path.Substring("/channels/".Length);
                        var slashIdx = rest.IndexOf('/');
                        if (slashIdx > 0)
                        {
                            var channel = WebUtility.UrlDecode(rest[..slashIdx]);
                            var subPath = rest[(slashIdx + 1)..];

                            if (subPath.StartsWith("recent", StringComparison.OrdinalIgnoreCase) && method == "GET")
                            {
                                int.TryParse(ctx.Request.QueryString["limit"], out var limit);
                                if (limit <= 0) limit = 50;
                                var recent = await _bus.GetRecentAsync(channel, limit, ct);
                                await WriteJson(ctx, 200, recent);
                                return;
                            }

                            if (subPath.StartsWith("stream", StringComparison.OrdinalIgnoreCase) && method == "GET")
                            {
                                await HandleSseStreamAsync(ctx, channel, ct);
                                return;
                            }
                        }
                    }

                    // /agents/{id}/heartbeat
                    if (path.StartsWith("/agents/", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        var rest = path.Substring("/agents/".Length);
                        var slashIdx = rest.IndexOf('/');
                        if (slashIdx > 0 && rest[(slashIdx + 1)..].Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
                        {
                            var agentId = WebUtility.UrlDecode(rest[..slashIdx]);
                            var hb = await ReadJson<HeartbeatDto>(ctx);
                            await _bus.HeartbeatAsync(agentId, hb.Status, ct);
                            await WriteJson(ctx, 200, new { ok = true });
                            return;
                        }
                    }

                    await WriteJson(ctx, 404, new { error = "Not found", path });
                    return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HerculesBus] HTTP handler error: {ex.GetType().Name}: {ex.Message}");
            try { await WriteJson(ctx, 500, new { error = ex.Message }); } catch { }
        }
    }

    private bool Authenticate(HttpListenerContext ctx)
    {
        if (_validTokens.Count == 0) return true; // dev mode: open

        var token = ctx.Request.Headers["X-Hercules-Token"];
        if (string.IsNullOrEmpty(token)) return false;
        return _validTokens.Contains(token);
    }

    private async Task HandleSseStreamAsync(HttpListenerContext ctx, string channel, CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.Add("Cache-Control", "no-cache");
        ctx.Response.Headers.Add("Connection", "keep-alive");
        ctx.Response.SendChunked = true;

        var writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8) { AutoFlush = false };
        var heartbeatInterval = TimeSpan.FromSeconds(15);
        var lastHeartbeat = DateTime.UtcNow;

        await using var sub = _bus.Subscribe(channel, async (msg, _) =>
        {
            try
            {
                var json = JsonSerializer.Serialize(msg, JsonOpts);
                await writer.WriteAsync($"event: message\n");
                await writer.WriteAsync($"data: {json}\n\n");
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HerculesBus] SSE write error: {ex.Message}");
            }
        }, ct);

        // Heartbeat + cancellation loop
        try
        {
            while (!ct.IsCancellationRequested && ctx.Response.OutputStream.CanWrite)
            {
                if ((DateTime.UtcNow - lastHeartbeat) >= heartbeatInterval)
                {
                    await writer.WriteAsync(": heartbeat\n\n");
                    await writer.FlushAsync();
                    lastHeartbeat = DateTime.UtcNow;
                }
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { /* client disconnected */ }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static async Task WriteJson(HttpListenerContext ctx, int status, object body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(body, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task<T> ReadJson<T>(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(text)) return default!;
        return JsonSerializer.Deserialize<T>(text, JsonOpts) ?? throw new InvalidOperationException("Empty body");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        if (_acceptLoop != null) { try { await _acceptLoop; } catch { } }
        _cts.Dispose();
    }

    // ===== DTOs =====

    private sealed record AgentIdentityDto(
        string AgentId,
        string DisplayName,
        string[] Roles,
        string Token,
        string[]? SubscribedChannels = null)
    {
        public AgentIdentity ToIdentity() => new(AgentId, DisplayName, Roles, Token, SubscribedChannels);
    }

    private sealed record CreateChannelDto(string Name, string? Description, bool IsPrivate, string? CreatedBy);

    private sealed record HeartbeatDto(AgentStatus Status);

    private sealed record AgentMessageDto(
        string Id,
        string Channel,
        string SenderAgentId,
        string SenderName,
        string Kind,
        string Body,
        string? ReplyTo = null,
        string[]? Mentions = null,
        MessageAttachmentDto[]? Attachments = null,
        DateTimeOffset? Timestamp = null)
    {
        public AgentMessage ToMessage()
        {
            IReadOnlyList<string>? mentions = Mentions;
            IReadOnlyList<MessageAttachment>? attachments = Attachments?
                .Select(a => new MessageAttachment(a.Name, a.MimeType, a.Url, a.SizeBytes))
                .ToList();
            return new AgentMessage(
                Id: Id ?? "",
                Channel: Channel,
                SenderAgentId: SenderAgentId,
                SenderName: SenderName,
                Kind: Kind,
                Body: Body,
                ReplyTo: ReplyTo,
                Mentions: mentions,
                Attachments: attachments,
                Timestamp: Timestamp);
        }
    }

    private sealed record MessageAttachmentDto(string Name, string MimeType, string Url, long SizeBytes = 0);
}
