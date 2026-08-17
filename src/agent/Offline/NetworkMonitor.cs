using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Hercules.Offline;

/// <summary>
///     Monitors network connectivity by polling a configurable HTTP endpoint.
///     Raises <see cref="OnReconnected"/> when the network transitions from offline to online.
///     Raises <see cref="OnDisconnected"/> when the network transitions from online to offline.
/// </summary>
public sealed class NetworkMonitor : INetworkMonitor, IDisposable
{
    /// <summary>Имя named HttpClient-клиента для health-check polling (task_078).</summary>
    public const string HttpClientName = "network-monitor";

    private readonly OfflineSyncConfig _config;
    private readonly ILogger<NetworkMonitor> _log;
    private readonly IHttpClientFactory? _httpFactory;
    private readonly HttpClient? _ownedHttp; // legacy fallback
    private readonly object _lock = new();

    private volatile bool _isOnline;
    private volatile bool _disposed;

    /// <summary>Fired when network becomes available after being offline.</summary>
    public event EventHandler? OnReconnected;

    /// <summary>Fired when network goes offline.</summary>
    public event EventHandler? OnDisconnected;

    public NetworkMonitor(OfflineSyncConfig config, ILogger<NetworkMonitor> log)
        : this(config, log, httpFactory: null, http: null)
    {
    }

    /// <summary>
    ///     Legacy / DI-test ctor: explicit <paramref name="http"/> для unit-тестов.
    /// </summary>
    public NetworkMonitor(OfflineSyncConfig config, ILogger<NetworkMonitor> log, HttpClient? http)
        : this(config, log, httpFactory: null, http: http)
    {
    }

    /// <summary>
    ///     DI-friendly конструктор (task_078): использует <see cref="IHttpClientFactory"/>
    ///     named-клиент "network-monitor" с short-lived <see cref="HttpClient"/> per check.
    /// </summary>
    public NetworkMonitor(
        OfflineSyncConfig config,
        ILogger<NetworkMonitor> log,
        IHttpClientFactory httpFactory)
        : this(config, log, httpFactory, http: null)
    {
    }

    private NetworkMonitor(
        OfflineSyncConfig config,
        ILogger<NetworkMonitor> log,
        IHttpClientFactory? httpFactory,
        HttpClient? http)
    {
        _config = config;
        _log = log;
        _httpFactory = httpFactory;
        _ownedHttp = http ?? (httpFactory is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(config.NetworkPollTimeoutSeconds) }
            : null);
        _isOnline = false; // start assuming offline until first check
    }

    /// <summary>Короткоживущий клиент per check — через factory или legacy owned-экземпляр.</summary>
    private HttpClient ResolveClient()
    {
        if (_httpFactory is not null)
        {
            return _httpFactory.CreateClient(HttpClientName);
        }
        return _ownedHttp!;
    }

    /// <summary>Current connectivity state.</summary>
    public bool IsOnline
    {
        get { lock (_lock) return _isOnline; }
        private set
        {
            lock (_lock)
            {
                if (_isOnline == value) return;
                _isOnline = value;
            }
        }
    }

    /// <summary>
    ///     Check connectivity once. Returns true if reachable.
    ///     Raises OnReconnected / OnDisconnected on state change.
    /// </summary>
    public async Task<bool> CheckOnceAsync(CancellationToken ct = default)
    {
        var url = ResolveUrl();
        if (string.IsNullOrEmpty(url))
        {
            _log.LogDebug("NetworkMonitor: no URL configured, assuming offline");
            return false;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var client = ResolveClient();
            var resp = await client.SendAsync(req, ct);
            var reachable = resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized;

            if (reachable && !IsOnline)
            {
                _log.LogInformation("NetworkMonitor: connectivity restored ({Url})", url);
                IsOnline = true;
                OnReconnected?.Invoke(this, EventArgs.Empty);
            }
            else if (!reachable && IsOnline)
            {
                _log.LogWarning("NetworkMonitor: connectivity lost ({Url})", url);
                IsOnline = false;
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }

            return reachable;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsOnline)
            {
                _log.LogWarning(ex, "NetworkMonitor: connectivity check failed ({Url})", url);
                IsOnline = false;
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
            return false;
        }
    }

    private string? ResolveUrl()
    {
        if (!string.IsNullOrWhiteSpace(_config.NetworkPollUrl))
            return _config.NetworkPollUrl;

        // [task_087] Fall back to a well-known external endpoint instead of
        // returning null (which would make NetworkMonitor report offline forever).
        // The default points at Cloudflare 1.1.1.1; operators can override via
        // OfflineSync.NetworkFallbackPollUrl in appsettings.json.
        return string.IsNullOrWhiteSpace(_config.NetworkFallbackPollUrl) ? null : _config.NetworkFallbackPollUrl;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedHttp?.Dispose();
    }
}
