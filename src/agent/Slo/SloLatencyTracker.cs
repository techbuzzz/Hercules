using System.Collections.Concurrent;

namespace Hercules.Slo;

/// <summary>
///     Bounded, lock-free per-intent latency tracker (task_087).
///     Each intent owns a small ring buffer of <c>(timestamp, latencyMs)</c>
///     pairs. <see cref="GetP95Ms"/> walks the buffer in O(N) — fine for the
///     default 512 samples per intent and infrequent SLO evaluation calls.
///
///     Memory bound: <c>SampleCapacity * intents * 16 bytes</c> (~64 KB at
///     defaults for 8 distinct intents). Threads contend only on the
///     interlocked write position of the ring slot — no locks taken in
///     <see cref="RecordSample"/>.
/// </summary>
public sealed class SloLatencyTracker : ISloLatencyTracker
{
    private readonly int _capacityPerIntent;
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<string, Ring> _rings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Ring _globalRing; // for unfiltered queries

    public SloLatencyTracker(int capacityPerIntent = 512, TimeSpan? maxAge = null)
    {
        if (capacityPerIntent < 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityPerIntent), capacityPerIntent,
                "Capacity must be at least 16 to produce a meaningful P95.");
        }

        _capacityPerIntent = capacityPerIntent;
        _maxAge = maxAge ?? TimeSpan.FromHours(24);
        _globalRing = new Ring(_capacityPerIntent);
    }

    /// <inheritdoc />
    public int SampleCount => _globalRing.Count + _rings.Values.Sum(r => r.Count);

    /// <inheritdoc />
    public void Reset()
    {
        _globalRing.Reset();
        _rings.Clear();
    }

    /// <inheritdoc />
    public void RecordSample(string intent, double latencyMs)
    {
        if (double.IsNaN(latencyMs) || double.IsInfinity(latencyMs) || latencyMs < 0)
        {
            return; // ignore garbage
        }

        var now = DateTimeOffset.UtcNow;
        _globalRing.Add(now, latencyMs);

        var key = string.IsNullOrWhiteSpace(intent) ? "__global__" : intent;
        var ring = _rings.GetOrAdd(key, _ => new Ring(_capacityPerIntent));
        ring.Add(now, latencyMs);
    }

    /// <inheritdoc />
    public double GetP95Ms(string? intent = null, TimeSpan? window = null)
    {
        var effectiveWindow = window ?? _maxAge;

        // Non-positive window means "do not filter by age" — keeps callers
        // that pass TimeSpan.Zero or negative values from accidentally
        // excluding every sample (cutoff would land in the future).
        var cutoff = effectiveWindow > TimeSpan.Zero
            ? DateTimeOffset.UtcNow - effectiveWindow
            : DateTimeOffset.MinValue;

        if (string.IsNullOrWhiteSpace(intent))
        {
            return ComputeP95(_globalRing.Snapshot(cutoff));
        }

        if (!_rings.TryGetValue(intent, out var ring))
        {
            return 0;
        }
        return ComputeP95(ring.Snapshot(cutoff));
    }

    private static double ComputeP95(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        // Copy + sort — values.Count is bounded by ring capacity so allocation
        // is cheap and the sort dominates only at very high sample rates.
        var sorted = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            sorted[i] = values[i];
        }
        Array.Sort(sorted);

        // Nearest-rank percentile: index = ceil(0.95 * N) - 1, clamped.
        var rank = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
        if (rank < 0) rank = 0;
        if (rank >= sorted.Length) rank = sorted.Length - 1;
        return sorted[rank];
    }

    // ── Ring buffer ────────────────────────────────────────────────────────

    /// <summary>
    ///     Fixed-size ring of <c>(timestamp, latencyMs)</c> pairs.
    ///     Slot acquisition uses <see cref="Interlocked.Increment(ref int)"/>
    ///     on <c>_next</c> so concurrent writers do not need a lock. The
    ///     returned samples from <see cref="Snapshot"/> may include one
    ///     torn write per concurrent overflow boundary, which the percentile
    ///     calculation tolerates by-design (a single bad sample cannot
    ///     noticeably shift a 95th-percentile estimate).
    /// </summary>
    private sealed class Ring
    {
        private readonly Sample[] _samples;
        private int _next; // monotonic write index (0-based after the first Add)

        public Ring(int capacity)
        {
            _samples = new Sample[capacity];
        }

        public int Count => Math.Min(_next, _samples.Length);

        public void Add(DateTimeOffset timestamp, double latencyMs)
        {
            // Pre-increment: the new value of _next is the slot to write to.
            var newPos = Interlocked.Increment(ref _next);
            var idx = (newPos - 1) % _samples.Length;
            _samples[idx] = new Sample(timestamp, latencyMs);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _next, 0);
            Array.Clear(_samples, 0, _samples.Length);
        }

        public IReadOnlyList<double> Snapshot(DateTimeOffset cutoff)
        {
            // Snapshot _next under a fence: any concurrent writer that
            // pre-increments after this read will land in a slot we either
            // include (and we may see one extra in-flight sample) or skip
            // (if it wrapped past our snapshot length). Either is safe for
            // a percentile calculation over a bounded buffer.
            var liveCount = Math.Min(Volatile.Read(ref _next), _samples.Length);
            if (liveCount == 0)
            {
                return Array.Empty<double>();
            }

            // Two layout cases:
            //   • Buffer not yet full: samples occupy [0 .. liveCount-1].
            //   • Buffer full: oldest live sample is at _next % capacity;
            //     reading `liveCount` slots going forward (mod capacity)
            //     gives us the samples in chronological order.
            int startIdx;
            int visitCount;
            if (_next <= _samples.Length)
            {
                startIdx = 0;
                visitCount = _next;
            }
            else
            {
                startIdx = _next % _samples.Length;
                visitCount = liveCount;
            }

            var values = new List<double>(visitCount);
            for (var step = 0; step < visitCount; step++)
            {
                var idx = (startIdx + step) % _samples.Length;
                var s = _samples[idx];
                if (s.Timestamp >= cutoff)
                {
                    values.Add(s.LatencyMs);
                }
            }
            return values;
        }

        private readonly struct Sample
        {
            public readonly DateTimeOffset Timestamp;
            public readonly double LatencyMs;
            public Sample(DateTimeOffset ts, double ms) { Timestamp = ts; LatencyMs = ms; }
        }
    }
}
