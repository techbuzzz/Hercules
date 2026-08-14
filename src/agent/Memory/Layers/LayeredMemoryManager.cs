using System.Text;
using Hercules.LLM;

namespace Hercules.Memory.Layers;

/// <summary>
///     Facade composing all 4 memory layers. Provides a unified API for the agent.
///     Assembles context for LLM prompts and manages memory lifecycle.
/// </summary>
public sealed class LayeredMemoryManager
{
    private readonly IWorkingMemory _workingMemory;
    private readonly IDurableFactsStore _factsStore;
    private readonly IEpisodicStore _episodicStore;
    private readonly LayeredMemoryConfig _config;

    /// <summary>
    ///     Direct access to episodic store (for ContextBuilder compression).
    /// </summary>
    public IEpisodicStore EpisodicStore => _episodicStore;

    public LayeredMemoryManager(
        IWorkingMemory workingMemory,
        IDurableFactsStore factsStore,
        IEpisodicStore episodicStore,
        LayeredMemoryConfig? config = null)
    {
        _workingMemory = workingMemory;
        _factsStore = factsStore;
        _episodicStore = episodicStore;
        _config = config ?? new LayeredMemoryConfig();
    }

    /// <summary>
    ///     Build the complete memory context block for an LLM prompt.
    ///     Combines: durable facts + recent episodes + working memory.
    ///     Respects sensitivity redaction.
    /// </summary>
    public async Task<string> BuildContextBlockAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // Durable facts (non-expired, non-redacted)
        var facts = await _factsStore.SearchFactsAsync(includeExpired: false, ct: ct);
        var redactedFacts = facts
            .Where(f => !f.Entry.ShouldRedact(_config.SensitivityRedactionEnabled))
            .ToList();

        if (redactedFacts.Count > 0)
        {
            sb.AppendLine("=== УСТОЙЧИВЫЕ ФАКТЫ ===");
            foreach (var (key, value, entry) in redactedFacts)
            {
                sb.AppendLine($"- [{entry.Confidence}] {key}: {value.Trim()}");
            }

            sb.AppendLine();
        }

        // Recent episodes
        var episodes = await _episodicStore.GetRecentEpisodesAsync(_config.MaxEpisodesInContext, ct);
        if (episodes.Count > 0)
        {
            sb.AppendLine("=== КОНТЕКСТ ПРОШЛЫХ СЕССИЙ ===");
            foreach (var ep in episodes)
            {
                var prefix = !ep.Entry.ShouldRedact(_config.SensitivityRedactionEnabled)
                    ? $"[{ep.CreatedAt:yyyy-MM-dd}]"
                    : "[КОНФИДЕНЦИАЛЬНО]";
                sb.AppendLine($"{prefix} {ep.Summary.Trim()}");
            }

            sb.AppendLine();
        }

        // Working memory
        var working = _workingMemory.GetAll();
        var redactedWorking = working
            .Where(kvp => !kvp.Value.Entry.ShouldRedact(_config.SensitivityRedactionEnabled))
            .ToList();

        if (redactedWorking.Count > 0)
        {
            sb.AppendLine("=== РАБОЧАЯ ПАМЯТЬ ===");
            foreach (var (key, (value, entry)) in redactedWorking)
            {
                sb.AppendLine($"[{entry.Confidence}] {key}: {value.Trim()}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Store a durable fact.</summary>
    public Task StoreFactAsync(string key, string value, MemoryEntry entry, CancellationToken ct = default)
        => _factsStore.StoreFactAsync(key, value, entry, ct);

    /// <summary>Get a durable fact.</summary>
    public Task<(string? Value, MemoryEntry? Entry)?> GetFactAsync(string key, CancellationToken ct = default)
        => _factsStore.GetFactAsync(key, ct);

    /// <summary>Set a working memory entry.</summary>
    public void SetWorking(string key, string value, MemoryEntry? entry = null)
        => _workingMemory.Set(key, value, entry);

    /// <summary>Get a working memory entry.</summary>
    public string? GetWorking(string key) => _workingMemory.Get(key);

    /// <summary>Clear working memory (end of session).</summary>
    public void ClearWorking() => _workingMemory.Clear();

    /// <summary>Persist a session transcript as an episode.</summary>
    public Task AppendEpisodeAsync(string sessionId, string summary, MemoryEntry entry, CancellationToken ct = default)
        => _episodicStore.AppendEpisodeAsync(sessionId, summary, entry, ct);

    /// <summary>Cleanup expired facts.</summary>
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
        => _factsStore.CleanupExpiredAsync(ct);
}
