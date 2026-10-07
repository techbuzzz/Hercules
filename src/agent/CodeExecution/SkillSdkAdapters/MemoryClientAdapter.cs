using Hercules.Agent;
using Hercules.Memory.Layers;
using Hercules.SkillSdk;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution.SkillSdkAdapters;

/// <summary>
///     Agent-side implementation of <see cref="IMemoryClient"/> for file-based skills.
///     Scopes writes to the current session/durable/episodic layers.
/// </summary>
public sealed class MemoryClientAdapter : IMemoryClient
{
    private readonly string _sessionId;
    private readonly LayeredMemoryManager _layered;
    private readonly IDurableFactsStore _factsStore;
    private readonly IWorkingMemory _executionMemory;
    private readonly ILogger _logger;

    public MemoryClientAdapter(
        string sessionId,
        LayeredMemoryManager layered,
        IDurableFactsStore factsStore,
        ILogger logger)
    {
        _sessionId = sessionId;
        _layered = layered;
        _factsStore = factsStore;
        _logger = logger;
        _executionMemory = new WorkingMemoryService();
    }

    public Task SetAsync(string key, string value, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default)
    {
        var entry = new MemoryEntry("skill", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, DateTime.UtcNow, new List<string> { "skill" });

        switch (scope)
        {
            case SkillMemoryScope.Execution:
                _executionMemory.Set(key, value, entry);
                break;
            case SkillMemoryScope.Session:
                _layered.SetWorking(key, value, entry, _sessionId);
                break;
            case SkillMemoryScope.Durable:
                return _factsStore.StoreFactAsync(key, value, entry, ct);
            case SkillMemoryScope.Episodic:
                return _layered.AppendEpisodeAsync(_sessionId, value, entry, ct);
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown memory scope");
        }

        _logger.LogInformation("SkillSdk memory set: scope={Scope} key={Key}", scope, key);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default)
    {
        string? value = scope switch
        {
            SkillMemoryScope.Execution => _executionMemory.Get(key),
            SkillMemoryScope.Session => _layered.GetWorking(key, _sessionId),
            SkillMemoryScope.Durable => _factsStore.GetFactAsync(key, ct).GetAwaiter().GetResult()?.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Only Execution, Session and Durable scopes support direct Get")
        };

        return Task.FromResult(value);
    }

    public async Task<IReadOnlyList<SkillMemoryEntry>> SearchAsync(string? keyPrefix = null, string? tag = null, CancellationToken ct = default)
    {
        var results = new List<SkillMemoryEntry>();

        // Execution memory
        foreach (var (key, (value, entry)) in _executionMemory.GetAll())
        {
            if (Matches(key, value, entry, keyPrefix, tag))
            {
                results.Add(Map(key, value, entry, SkillMemoryScope.Execution));
            }
        }

        // Durable facts
        var matches = await _factsStore.SearchFactsAsync(tag, keyPrefix, includeExpired: false, ct);
        foreach (var (key, value, entry) in matches)
        {
            results.Add(Map(key, value, entry, SkillMemoryScope.Durable));
        }

        // Episodic
        var episodes = await _layered.EpisodicStore.GetRecentEpisodesAsync(int.MaxValue, ct);
        foreach (var ep in episodes)
        {
            if (Matches(ep.Summary, ep.Summary, ep.Entry, keyPrefix, tag))
            {
                results.Add(new SkillMemoryEntry
                {
                    Key = ep.SessionId,
                    Value = ep.Summary,
                    Scope = SkillMemoryScope.Episodic,
                    CreatedAt = ep.CreatedAt,
                    Tags = ep.Entry.Tags
                });
            }
        }

        return results;
    }

    private static bool Matches(string key, string value, MemoryEntry entry, string? keyPrefix, string? tag)
    {
        var prefixOk = string.IsNullOrWhiteSpace(keyPrefix) || key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase);
        var tagOk = string.IsNullOrWhiteSpace(tag) || entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        return prefixOk && tagOk;
    }

    private static SkillMemoryEntry Map(string key, string value, MemoryEntry entry, SkillMemoryScope scope)
    {
        return new SkillMemoryEntry
        {
            Key = key,
            Value = value,
            Scope = scope,
            CreatedAt = entry.CreatedAt,
            Tags = entry.Tags
        };
    }
}
