using System.Collections.Concurrent;

namespace Hercules.Mesh.Router;

/// <summary>
///     Per-peer rolling health tracker for mesh router decisions.
///     Thread-safe; records successes and failures from transport results.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_043 § RouterHealthTracker.
/// </summary>
public sealed class RouterHealthTracker
{
    private readonly ConcurrentDictionary<string, PeerHealthRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Window size for rolling success rate calculation. Default: 20.</summary>
    public int WindowSize { get; set; } = 20;

    /// <summary>
    ///     Record a successful call for a peer.
    /// </summary>
    public void RecordSuccess(string agentId)
    {
        GetOrCreate(agentId).RecordSuccess();
    }

    /// <summary>
    ///     Record a successful call for a peer with observed latency.
    /// </summary>
    public void RecordSuccess(string agentId, int latencyMs)
    {
        GetOrCreate(agentId).RecordSuccess(latencyMs);
    }

    /// <summary>
    ///     Record a failed call for a peer.
    /// </summary>
    public void RecordFailure(string agentId)
    {
        GetOrCreate(agentId).RecordFailure();
    }

    /// <summary>
    ///     Get the current health score (0.0 – 1.0) for a peer.
    ///     Returns 1.0 for unknown peers (optimistic default).
    /// </summary>
    public double GetHealthScore(string agentId)
    {
        return _records.TryGetValue(agentId, out PeerHealthRecord? record)
            ? record.HealthScore
            : 1.0;
    }

    /// <summary>
    ///     Get the average latency (ms) for a peer over the rolling window.
    ///     Returns 0 for unknown peers.
    /// </summary>
    public int GetAverageLatencyMs(string agentId)
    {
        return _records.TryGetValue(agentId, out PeerHealthRecord? record)
            ? record.AverageLatencyMs
            : 0;
    }

    /// <summary>
    ///     Whether the peer circuit is open (excluded from routing).
    /// </summary>
    public bool IsCircuitOpen(string agentId, CircuitState circuitState)
    {
        return circuitState == CircuitState.Open;
    }

    private PeerHealthRecord GetOrCreate(string agentId)
    {
        return _records.GetOrAdd(agentId, _ => new PeerHealthRecord(WindowSize));
    }
}

/// <summary>
///     Rolling health record for a single peer.
/// </summary>
internal sealed class PeerHealthRecord
{
    private readonly int[] _window; // 1 = success, 0 = failure
    private int _index;
    private int _count;
    private int _successes;
    private readonly int[] _latencies;
    private int _latencyIndex;
    private int _latencyCount;
    private long _latencySum;

    public PeerHealthRecord(int windowSize)
    {
        _window = new int[windowSize];
        _latencies = new int[windowSize];
    }

    public double HealthScore
    {
        get
        {
            if (_count == 0)
            {
                return 1.0;
            }

            return Math.Round((double)_successes / _count, 4);
        }
    }

    public int AverageLatencyMs
    {
        get
        {
            return _latencyCount == 0 ? 0 : (int)(_latencySum / _latencyCount);
        }
    }

    public void RecordSuccess(int latencyMs = 0)
    {
        _window[_index % _window.Length] = 1;
        _index++;
        _count = Math.Min(_count + 1, _window.Length);
        _successes++;

        if (latencyMs > 0)
        {
            _latencies[_latencyIndex % _latencies.Length] = latencyMs;
            _latencyIndex++;
            _latencyCount = Math.Min(_latencyCount + 1, _latencies.Length);
            _latencySum += latencyMs;
        }
    }

    public void RecordFailure()
    {
        _window[_index % _window.Length] = 0;
        _index++;
        _count = Math.Min(_count + 1, _window.Length);
    }
}
