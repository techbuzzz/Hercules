using System.Text;
using Hercules.Agent;
using Hercules.LLM;
using Microsoft.Extensions.ObjectPool;

namespace Hercules.Memory.Layers;

/// <summary>
///     Facade composing all 4 memory layers. Provides a unified API for the agent.
///     Assembles context for LLM prompts and manages memory lifecycle.
///     <para>
///         task_075 H6 fix: when constructed with <see cref="ISessionStateStore" />,
///         working memory is resolved per session, eliminating the previous
///         captive-dependency (singleton <c>LayeredMemoryManager</c> capturing a
///         scoped <c>IWorkingMemory</c> shared across all HTTP requests).
///     </para>
/// </summary>
public sealed class LayeredMemoryManager
{
    // task_084: pooled StringBuilder for context-block assembly.
    private static readonly ObjectPool<StringBuilder> SbPool =
        new DefaultObjectPoolProvider().CreateStringBuilderPool();

    private readonly IWorkingMemory? _legacyWorkingMemory;
    private readonly ISessionStateStore? _sessionStates;
    private readonly IDurableFactsStore _factsStore;
    private readonly IEpisodicStore _episodicStore;
    private readonly LayeredMemoryConfig _config;

    /// <summary>
    ///     Direct access to episodic store (for ContextBuilder compression).
    /// </summary>
    public IEpisodicStore EpisodicStore => _episodicStore;

    /// <summary>Legacy single-session ctor (kept for tests / CLI single-tenant mode).</summary>
    public LayeredMemoryManager(
        IWorkingMemory workingMemory,
        IDurableFactsStore factsStore,
        IEpisodicStore episodicStore,
        LayeredMemoryConfig? config = null)
    {
        _legacyWorkingMemory = workingMemory;
        _sessionStates = null;
        _factsStore = factsStore;
        _episodicStore = episodicStore;
        _config = config ?? new LayeredMemoryConfig();
    }

    /// <summary>
    ///     task_075 H6 fix: per-session ctor. Looks up the per-session
    ///     <c>IWorkingMemory</c> from the store on every call so that parallel
    ///     HTTP requests can never leak working-memory entries across sessions.
    /// </summary>
    public LayeredMemoryManager(
        ISessionStateStore sessionStates,
        IDurableFactsStore factsStore,
        IEpisodicStore episodicStore,
        LayeredMemoryConfig? config = null)
    {
        _legacyWorkingMemory = null;
        _sessionStates = sessionStates ?? throw new ArgumentNullException(nameof(sessionStates));
        _factsStore = factsStore;
        _episodicStore = episodicStore;
        _config = config ?? new LayeredMemoryConfig();
    }

    private IWorkingMemory ResolveWorkingMemory(string? sessionId)
    {
        if (_sessionStates is not null)
        {
            return _sessionStates.GetOrCreate(sessionId ?? "default").WorkingMemory;
        }
        return _legacyWorkingMemory ?? throw new InvalidOperationException("Working memory not configured");
    }

    /// <summary>
    /// Build the complete memory context block for an LLM prompt.
    /// Uses legacy (process-wide) working memory.
    /// </summary>
    public Task<string> BuildContextBlockAsync(CancellationToken ct = default) =>
        BuildContextBlockAsync(sessionId: null, ct);

    /// <summary>
    /// task_075 H6 fix: per-session context assembly. Working memory entries are
    /// scoped to <paramref name="sessionId" /> so that concurrent requests cannot
    /// observe each other's scratchpad / reasoning.
    /// </summary>
    public async Task<string> BuildContextBlockAsync(string? sessionId, CancellationToken ct = default)
    {
        // task_084: pooled StringBuilder.
        var sb = SbPool.Get();
        try
        {
            // Durable facts (non-expired, non-redacted) — shared across sessions (intentional).
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

            // Recent episodes — shared across sessions.
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

            // Working memory — task_075 H6 fix: per-session view.
            var workingMemory = ResolveWorkingMemory(sessionId);
            var working = workingMemory.GetAll();
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
        finally
        {
            SbPool.Return(sb);
        }
    }

    /// <summary>Store a durable fact.</summary>
    public Task StoreFactAsync(string key, string value, MemoryEntry entry, CancellationToken ct = default)
        => _factsStore.StoreFactAsync(key, value, entry, ct);

    /// <summary>Get a durable fact.</summary>
    public Task<(string? Value, MemoryEntry? Entry)?> GetFactAsync(string key, CancellationToken ct = default)
        => _factsStore.GetFactAsync(key, ct);

    /// <summary>
    ///     Set a working memory entry for the given session (task_075 H6: per-session).
    ///     When the manager was constructed with the legacy single-session ctor, the
    ///     sessionId is ignored and the legacy working memory is used.
    /// </summary>
    public void SetWorking(string key, string value, MemoryEntry? entry = null, string? sessionId = null)
        => ResolveWorkingMemory(sessionId).Set(key, value, entry);

    /// <summary>Get a working memory entry for the given session.</summary>
    public string? GetWorking(string key, string? sessionId = null)
        => ResolveWorkingMemory(sessionId).Get(key);

    /// <summary>
    ///     Clear working memory (end of session). For per-session managers, only
    ///     the given session is cleared; for legacy managers the process-wide
    ///     working memory is cleared.
    /// </summary>
    public void ClearWorking(string? sessionId = null)
        => ResolveWorkingMemory(sessionId).Clear();

    /// <summary>Persist a session transcript as an episode.</summary>
    public Task AppendEpisodeAsync(string sessionId, string summary, MemoryEntry entry, CancellationToken ct = default)
        => _episodicStore.AppendEpisodeAsync(sessionId, summary, entry, ct);

    /// <summary>Cleanup expired facts.</summary>
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
        => _factsStore.CleanupExpiredAsync(ct);
}
