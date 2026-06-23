using System.Collections.Concurrent;
using HerculesBus.Core;

namespace HerculesBus.InMemory;

/// <summary>
///     In-memory реализация IAgentRegistry. Thread-safe.
///     В V3.2 — заменяется на SqliteAgentRegistry или distributed registry (Redis).
/// </summary>
public sealed class InMemoryAgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new(StringComparer.OrdinalIgnoreCase);

    public Task<AgentRegistrationResult> RegisterAsync(AgentIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var isNew = !_agents.ContainsKey(identity.AgentId);
        var now = DateTimeOffset.UtcNow;

        var info = new AgentInfo(
            AgentId: identity.AgentId,
            DisplayName: identity.DisplayName,
            Roles: identity.Roles,
            Status: AgentStatus.Online,
            RegisteredAt: isNew ? now : (_agents[identity.AgentId].RegisteredAt),
            LastSeen: now,
            SubscribedChannels: identity.SubscribedChannels);

        _agents[identity.AgentId] = info;

        return Task.FromResult(new AgentRegistrationResult(isNew, info));
    }

    public Task HeartbeatAsync(string agentId, AgentStatus status, CancellationToken ct = default)
    {
        if (!_agents.TryGetValue(agentId, out var existing))
            return Task.CompletedTask; // unknown agent — ignore

        _agents[agentId] = existing with
        {
            Status = status,
            LastSeen = DateTimeOffset.UtcNow
        };

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentInfo>> ListAsync(bool includeOffline = true, CancellationToken ct = default)
    {
        var all = _agents.Values.AsEnumerable();
        if (!includeOffline)
            all = all.Where(a => a.IsOnline);
        return Task.FromResult<IReadOnlyList<AgentInfo>>(all.OrderBy(a => a.AgentId).ToList());
    }

    public Task<AgentInfo?> GetAsync(string agentId, CancellationToken ct = default)
    {
        _agents.TryGetValue(agentId, out var info);
        return Task.FromResult(info);
    }
}
