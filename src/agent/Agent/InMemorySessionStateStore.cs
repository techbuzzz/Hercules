using System.Collections.Concurrent;
using Hercules.Memory.Layers;
using Microsoft.Extensions.Logging;

namespace Hercules.Agent;

/// <summary>
///     Default in-memory <see cref="ISessionStateStore" /> implementation.
///     Uses <see cref="ConcurrentDictionary{TKey, TValue}" /> for thread-safe
///     access. Best-effort eviction once <see cref="MaxSessions" /> is exceeded
///     removes the oldest created session.
/// </summary>
public sealed class InMemorySessionStateStore : ISessionStateStore
{
    private readonly ConcurrentDictionary<string, SessionState> _store = new();
    private readonly int _maxSessions;
    private readonly ILogger<InMemorySessionStateStore>? _logger;

    public InMemorySessionStateStore(int maxSessions = 1024, ILogger<InMemorySessionStateStore>? logger = null)
    {
        if (maxSessions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSessions), "maxSessions must be >= 1");
        }

        _maxSessions = maxSessions;
        _logger = logger;
    }

    public int Count => _store.Count;

    public SessionState GetOrCreate(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId must be non-empty", nameof(sessionId));
        }

        var state = _store.GetOrAdd(sessionId, id => new SessionState(id));

        if (_store.Count > _maxSessions)
        {
            EvictOldest();
        }

        return state;
    }

    public bool Remove(string sessionId)
    {
        return _store.TryRemove(sessionId, out _);
    }

    public IEnumerable<SessionState> ListSessions()
    {
        return _store.Values.ToList();
    }

    private void EvictOldest()
    {
        // Find the oldest session by CreatedAt and remove it. Best-effort: log if eviction fails.
        SessionState? oldest = null;
        foreach (var state in _store.Values)
        {
            if (oldest is null || state.CreatedAt < oldest.CreatedAt)
            {
                oldest = state;
            }
        }

        if (oldest is not null && _store.TryRemove(oldest.SessionId, out _))
        {
            _logger?.LogWarning(
                "[SessionStateStore] Max sessions ({Max}) exceeded — evicted oldest session {SessionId}",
                _maxSessions, oldest.SessionId);
        }
    }
}
