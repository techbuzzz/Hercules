namespace Hercules.Mesh;

/// <summary>
///     Фасад над низкоуровневым <see cref="CapabilityRegistry"/>.
///     Реализует <see cref="ICapabilityRegistryService"/> и используется в DI.
/// </summary>
public sealed class CapabilityRegistryService : ICapabilityRegistryService
{
    private readonly CapabilityRegistry _store;

    public CapabilityRegistryService(CapabilityRegistry store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public RegistryAgentFullEntry? GetEntry(string agentId) => _store.GetFull(agentId);

    public IReadOnlyList<RegistryAgentFullEntry> ListAll() => _store.ListAllAgents();

    public void UpdateHealthStatus(string agentId, AgentHealthStatus status, string? error = null, int? consecutiveFailures = null)
        => _store.UpdateHealthStatus(agentId, status, error, consecutiveFailures);

    public void UpdateTrustLevel(string agentId, string trustLevel)
        => _store.UpdateTrustLevel(agentId, trustLevel);

    public void UpdateCostHint(string agentId, decimal costUsd)
        => _store.UpdateCostHint(agentId, costUsd);

    public void UpdateLatencyHint(string agentId, int latencyMs)
        => _store.UpdateLatencyHint(agentId, latencyMs);

    public void SetExpiry(string agentId, int expirySeconds)
        => _store.SetExpiry(agentId, expirySeconds);

    public void Touch(string agentId) => _store.Touch(agentId);

    public bool Remove(string agentId) => _store.Remove(agentId);

    public List<RegistryAgentEntry> FindByCapability(string capabilityName)
        => _store.FindByCapability(capabilityName);

    public List<RegistryAgentEntry> FindByPhrase(string phrase)
        => _store.FindByPhrase(phrase);

    public List<RegistryCapabilityEntry> ListCapabilities(string agentId)
        => _store.ListCapabilities(agentId);

    public void Register(AgentManifest manifest) => _store.Register(manifest);

    public int CleanupExpired() => _store.CleanupExpired();
}
