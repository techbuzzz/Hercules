namespace Hercules.Cache;

/// <summary>
///     Task 028: Конфигурация unified caching infrastructure.
///     Controls TTL, scope, sensitivity rules and invalidation per cache class.
/// </summary>
public sealed class CacheConfig
{
    /// <summary>Enable the unified cache service. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Default TTL in seconds for all cache classes without an explicit override. Default: 300 (5 min).</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>Maximum number of entries across all cache classes. Default: 10 000.</summary>
    public int MaxEntries { get; set; } = 10_000;

    /// <summary>Background cleanup interval in seconds. Default: 60.</summary>
    public int CleanupIntervalSeconds { get; set; } = 60;

    /// <summary>Enable sliding expiration (touch entry on access). Default: true.</summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    ///     Per-class TTL overrides in seconds.
    ///     Key = CacheClass name, Value = TTL seconds.
    ///     If missing, falls back to DefaultTtlSeconds.
    /// </summary>
    public Dictionary<string, int> TtlByClass { get; set; } = new()
    {
        [nameof(CacheClass.Embedding)]           = 3600,    // 1 h — embeddings change rarely
        [nameof(CacheClass.RoutingDecision)]     = 30,      // 30 s — fast-changing
        [nameof(CacheClass.LlmCapability)]       = 86400,   // 24 h — provider capabilities change rarely
        [nameof(CacheClass.LlmPromptPrefix)]     = 86400,   // 24 h — static per provider/model
        [nameof(CacheClass.DeterministicToolResult)] = 300, // 5 min
    };

    /// <summary>
    ///     Per-class sensitivity levels.
    ///     Key = CacheClass name, Value = SensitivityLevel name.
    ///     Used to enforce retention and invalidation rules.
    /// </summary>
    public Dictionary<string, string> SensitivityByClass { get; set; } = new()
    {
        [nameof(CacheClass.Embedding)]           = nameof(SensitivityLevel.Public),
        [nameof(CacheClass.RoutingDecision)]     = nameof(SensitivityLevel.Public),
        [nameof(CacheClass.LlmCapability)]       = nameof(SensitivityLevel.Public),
        [nameof(CacheClass.LlmPromptPrefix)]     = nameof(SensitivityLevel.Public),
        [nameof(CacheClass.DeterministicToolResult)] = nameof(SensitivityLevel.Sensitive),
    };
}
