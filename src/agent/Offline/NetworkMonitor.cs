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
    private readonly OfflineSyncConfig _config;
    private readonly ILogger<NetworkMonitor> _log;
    private readonly HttpClient _http;
    private readonly object _lock = new();

    private volatile bool _isOnline;
    private volatile bool _disposed;

    /// <summary>Fired when network becomes available after being offline.</summary>
    public event EventHandler? OnReconnected;

    /// <summary>Fired when network goes offline.</summary>
    public event EventHandler? OnDisconnected;

    public NetworkMonitor(OfflineSyncConfig config, ILogger<NetworkMonitor> log, HttpClient? http = null)
    {
        _config = config;
        _log = log;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(config.NetworkPollTimeoutSeconds) };
        _isOnline = false; // start assuming offline until first check
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
            var resp = await _http.SendAsync(req, ct);
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

        // Fallback: use Mesh bus health endpoint if available
        // The actual URL will be provided via NetworkPollUrl in config
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
