using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hercules.Config;
using Hercules.Memory.Layers;
using Hercules.Mesh.Transport;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh;

/// <summary>
///     Sensitivity level for shared memory facts.
///     Restricted and Sensitive facts require explicit allow-listing per agent.
/// </summary>
public enum SharedMemorySensitivity
{
    /// <summary>Can be shared freely with trusted agents.</summary>
    Public,
    /// <summary>Internal business context; restricted agents may not receive.</summary>
    Internal,
    /// <summary>Requires explicit allow-listing; never broadcast by default.</summary>
    Sensitive,
    /// <summary>Must never be shared between agents.</summary>
    Restricted
}

/// <summary>
///     Факт памяти для синхронизации между доверенными агентами.
///     Только избранные факты (не вся память) — пользователь явно отмечает факты для shared.
/// </summary>
public sealed class SharedMemoryFact
{
    /// <summary>Уникальный идентификатор факта (12-char hex).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Категория: "profile" | "entities" | "preferences" | "custom".</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "profile";

    /// <summary>Текст факта (markdown).</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>AgentId-источник (кто опубликовал факт).</summary>
    [JsonPropertyName("sourceAgent")]
    public string SourceAgent { get; set; } = "";

    /// <summary>Доверенные агенты, которым разрешено читать этот факт (empty = всем доверенным).</summary>
    [JsonPropertyName("allowedAgents")]
    public List<string> AllowedAgents { get; set; } = new();

    /// <summary>Время создания/обновления (UTC ISO 8601).</summary>
    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    /// <summary>Версия (для conflict resolution при sync).</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    ///     Время создания факта (UTC ISO 8601). Используется для provenance tracking.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    /// <summary>
    ///     TTL в минутах. 0 = permanent.
    /// </summary>
    [JsonPropertyName("ttlMinutes")]
    public int TtlMinutes { get; set; } = 0;

    /// <summary>
    ///     UTC ISO 8601 deadline: если текущее время > ExpiresAt, факт игнорируется.
    ///     Вычисляется как CreatedAt + TtlMinutes.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    /// <summary>
    ///     Sensitivity level для access control. task_051.
    /// </summary>
    [JsonPropertyName("sensitivity")]
    public string Sensitivity { get; set; } = "Internal";

    /// <summary>
    ///     Источник факта: "session_extract", "user", "skill:{id}", "agent", etc.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "agent";

    /// <summary>Произвольные теги для фильтрации.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>True если факт просрочен по TTL.</summary>
    [JsonIgnore]
    public bool IsExpired
    {
        get
        {
            if (TtlMinutes <= 0 || string.IsNullOrEmpty(ExpiresAt)) return false;
            return !DateTimeOffset.TryParse(ExpiresAt, out var deadline) || DateTimeOffset.UtcNow > deadline;
        }
    }
}

/// <summary>
///     Shared Memory Sync — опциональная синхронизация избранных фактов памяти
///     между доверенными агентами в mesh.
///     Только факты, явно отмеченные как shared, синхронизируются — приватные данные остаются локальными.
///     Спецификация: docs/ROADMAP-RU.md Phase 4 #21.
/// </summary>
public sealed class SharedMemorySync : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly CapabilityRegistry _registry;
    private readonly ITransport _transport;
    private readonly AgentManifestService _manifestService;
    private readonly SharedMemorySyncConfig _config;
    private readonly ILogger<SharedMemorySync> _logger;

    public SharedMemorySync(
        string dataRoot,
        CapabilityRegistry registry,
        ITransport transport,
        AgentManifestService manifestService,
        SharedMemorySyncConfig config,
        ILogger<SharedMemorySync> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));
        _config = config ?? new SharedMemorySyncConfig();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var sharedDir = Path.Combine(dataRoot, Hercules.BuiltIn.MemorySubdir, Hercules.BuiltIn.SharedMemorySubdir);
        Directory.CreateDirectory(sharedDir);
        SharedMemoryPath = Path.Combine(sharedDir, Hercules.BuiltIn.SharedFactsFileName);
    }

    /// <summary>Путь к файлу локальных shared-фактов.</summary>
    public string SharedMemoryPath { get; }

    public void Dispose() { }

    /// <summary>
    ///     Опубликовать факт памяти для синхронизации с доверенными агентами.
    ///     Факт сохраняется локально и отправляется всем доверенным peer'ам.
    /// </summary>
    /// <param name="category">Namespace/category: profile, entities, preferences, custom.</param>
    /// <param name="content">Markdown content of the fact.</param>
    /// <param name="allowedAgents">Optional explicit allow-list (empty = all trusted agents).</param>
    /// <param name="ttlMinutes">TTL in minutes. 0 = permanent. Defaults to config value.</param>
    /// <param name="sensitivity">Sensitivity level. Defaults to Internal.</param>
    /// <param name="source">Provenance source tag. Defaults to "agent".</param>
    /// <param name="tags">Arbitrary tags for filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SharedMemoryFact> PublishFactAsync(
        string category,
        string content,
        List<string>? allowedAgents = null,
        int? ttlMinutes = null,
        string sensitivity = "Internal",
        string source = "agent",
        List<string>? tags = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var ttl = ttlMinutes ?? _config.DefaultTtlMinutes;
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = ttl > 0 ? createdAt.AddMinutes(ttl).ToString("o") : null;

        var fact = new SharedMemoryFact
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Category = category,
            Content = content,
            SourceAgent = _manifestService.Current.AgentId,
            AllowedAgents = allowedAgents ?? new List<string>(),
            UpdatedAt = createdAt.ToString("o"),
            CreatedAt = createdAt.ToString("o"),
            TtlMinutes = ttl,
            ExpiresAt = expiresAt,
            Sensitivity = sensitivity,
            Source = source,
            Tags = tags ?? new List<string>()
        };

        // Block Restricted facts from being published
        if (fact.Sensitivity.Equals("Restricted", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[SharedMemory] Blocked publishing Restricted fact {FactId}", fact.Id);
            return EmptyFact(fact.Id, category, content);
        }

        // Check max facts limit
        var facts = LoadLocalFacts(includeExpired: false);
        if (facts.Count >= _config.MaxFactsPerAgent)
        {
            _logger.LogWarning("[SharedMemory] Max facts limit reached ({Limit}), not publishing {FactId}",
                _config.MaxFactsPerAgent, fact.Id);
            return EmptyFact(fact.Id, category, content);
        }

        // Save locally
        var allFacts = LoadLocalFacts(includeExpired: false);
        allFacts[fact.Id] = fact;
        SaveLocalFacts(allFacts);

        // Broadcast to peers
        await BroadcastFactAsync(fact, ct);

        return fact;
    }

    /// <summary>
    ///     Принять факт от peer-агента (вызывается при получении через intent или API).
    ///     Если факт уже есть и версия новее — обновляем.
    ///     Факты с Sensitivity=Restricted не принимаются.
    /// </summary>
    public SharedMemoryFact? ReceiveFact(SharedMemoryFact incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        // Reject Restricted facts at the gate
        if (incoming.Sensitivity.Equals("Restricted", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[SharedMemory] Rejected Restricted fact {FactId} from {Source}",
                incoming.Id, incoming.SourceAgent);
            return null;
        }

        // Enforce MaxAllowedSensitivity from config
        if (!IsSensitivityAllowed(incoming.Sensitivity))
        {
            _logger.LogDebug("[SharedMemory] Rejected fact {FactId} sensitivity={Sensitivity} (max={Max})",
                incoming.Id, incoming.Sensitivity, _config.MaxAllowedSensitivity);
            return null;
        }

        // TTL check: reject expired facts
        if (incoming.IsExpired)
        {
            _logger.LogDebug("[SharedMemory] Rejected expired fact {FactId}", incoming.Id);
            return null;
        }

        Dictionary<string, SharedMemoryFact> facts = LoadLocalFacts(includeExpired: true);
        if (facts.TryGetValue(incoming.Id, out SharedMemoryFact? existing))
        {
            if (incoming.Version <= existing.Version)
            {
                return null; // We already have a newer version
            }
        }

        facts[incoming.Id] = incoming;
        SaveLocalFacts(facts);
        return incoming;
    }

    /// <summary>Получить все не-просроченные локальные shared-факты (с фильтрацией sensitivity).</summary>
    public List<SharedMemoryFact> GetLocalFacts()
    {
        return LoadLocalFacts(includeExpired: false)
            .Values
            .Where(f => IsSensitivityAllowed(f.Sensitivity))
            .ToList();
    }

    /// <summary>
    ///     Получить факты, доступные указанному агенту (для отправки по запросу).
    ///     Если allowedAgents пуст — факт доступен всем. Иначе — только перечисленным.
    ///     Факты фильтруются по sensitivity.
    /// </summary>
    public List<SharedMemoryFact> GetFactsForAgent(string targetAgentId)
    {
        return LoadLocalFacts(includeExpired: false)
            .Values
            .Where(f => IsSensitivityAllowed(f.Sensitivity))
            .Where(f => f.AllowedAgents.Count == 0 ||
                        f.AllowedAgents.Contains(targetAgentId, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    ///     Удалить факт (перестать синхронизировать).
    /// </summary>
    public bool RemoveFact(string factId)
    {
        Dictionary<string, SharedMemoryFact> facts = LoadLocalFacts(includeExpired: true);
        var removed = facts.Remove(factId);
        if (removed)
        {
            SaveLocalFacts(facts);
        }

        return removed;
    }

    /// <summary>
    ///     Синхронизировать: запросить shared-факты у всех доверенных peer'ов.
    ///     Возвращает число принятых фактов.
    /// </summary>
    public async Task<int> SyncFromPeersAsync(CancellationToken ct = default)
    {
        var ownAgentId = _manifestService.Current.AgentId;
        var peers = _registry.ListAgents()
            .Where(a => !a.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var received = 0;
        foreach (RegistryAgentEntry peer in peers)
        {
            try
            {
                var envelope = new IntentEnvelope
                {
                    RequestId = IntentIds.NewRequestId(),
                    Sender = ownAgentId,
                    Intent = "shared-memory-fetch",
                    Payload = JsonSerializer.Serialize(new { requesterAgentId = ownAgentId }),
                    Deadline = DateTimeOffset.UtcNow.AddMilliseconds(10_000)
                };

                var result = await _transport.SendAsync(peer.AgentId, envelope, ct).ConfigureAwait(false);
                IntentResponse? response = result.IsSuccess ? result.Response : null;
                if (response is not null && !string.IsNullOrEmpty(response.Result))
                {
                    List<SharedMemoryFact>? peerFacts = JsonSerializer.Deserialize<List<SharedMemoryFact>>(
                        response.Result,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (peerFacts is not null)
                    {
                        foreach (SharedMemoryFact fact in peerFacts)
                        {
                            SharedMemoryFact? accepted = ReceiveFact(fact);
                            if (accepted is not null)
                            {
                                received++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // [task_087] log the error instead of silently swallowing it.
                // Per-peer failures should not abort the overall sync loop, but
                // the operator must see why a particular peer was skipped.
                _logger.LogWarning(ex,
                    "[SharedMemory] Skip peer {PeerId} on sync error ({ErrorType})",
                    peer.AgentId, ex.GetType().Name);
            }
        }

        return received;
    }

    /// <summary>Отправить факт всем доверенным peer'ам (broadcast).</summary>
    private async Task BroadcastFactAsync(SharedMemoryFact fact, CancellationToken ct)
    {
        var ownAgentId = _manifestService.Current.AgentId;
        var peers = _registry.ListAgents()
            .Where(a => !a.AgentId.Equals(ownAgentId, StringComparison.OrdinalIgnoreCase))
            .Where(a => fact.AllowedAgents.Count == 0 ||
                        fact.AllowedAgents.Contains(a.AgentId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var envelope = new IntentEnvelope
        {
            RequestId = IntentIds.NewRequestId(),
            Sender = ownAgentId,
            Intent = "shared-memory-push",
            Payload = JsonSerializer.Serialize(fact, JsonOpts)
        };

        foreach (RegistryAgentEntry peer in peers)
        {
            try
            {
                var result = await _transport.SendAsync(peer.AgentId, envelope, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    _logger.LogDebug("[SharedMemory] Push to {PeerId} failed: {Error}", peer.AgentId, result.ErrorMessage);
                }
            }
            catch
            {
                /* best effort */
            }
        }
    }

    /// <summary>
    ///     Load facts from disk. Optionally filter out expired facts.
    /// </summary>
    /// <param name="includeExpired">If false, removes expired facts from the returned dictionary.</param>
    private Dictionary<string, SharedMemoryFact> LoadLocalFacts(bool includeExpired)
    {
        if (!File.Exists(SharedMemoryPath))
        {
            return new Dictionary<string, SharedMemoryFact>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(SharedMemoryPath);
            var list = JsonSerializer.Deserialize<List<SharedMemoryFact>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<SharedMemoryFact>();

            var dict = list
                .Where(f => includeExpired || !f.IsExpired)
                .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

            // Prune expired facts from disk on read
            if (!includeExpired)
            {
                var pruned = list.Where(f => !f.IsExpired).ToList();
                if (pruned.Count != list.Count)
                {
                    SaveLocalFacts(dict);
                }
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, SharedMemoryFact>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveLocalFacts(Dictionary<string, SharedMemoryFact> facts)
    {
        var json = JsonSerializer.Serialize(facts.Values.ToList(), JsonOpts);
        var temp = SharedMemoryPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, SharedMemoryPath, true);
    }

    /// <summary>
    ///     Check whether a sensitivity level is allowed based on config.
    ///     Ordering: Restricted > Sensitive > Internal > Public.
    /// </summary>
    private bool IsSensitivityAllowed(string sensitivity)
    {
        var maxLevel = ParseSensitivity(_config.MaxAllowedSensitivity);
        var factLevel = ParseSensitivity(sensitivity);
        return factLevel <= maxLevel;
    }

    private static int ParseSensitivity(string s)
    {
        return s?.ToLowerInvariant() switch
        {
            "public" => 0,
            "internal" => 1,
            "sensitive" => 2,
            "restricted" => 3,
            _ => 1 // Default to Internal
        };
    }

    /// <summary>Create a stub fact for blocked/rejected publishes (not saved locally).</summary>
    private static SharedMemoryFact EmptyFact(string id, string category, string content)
    {
        return new SharedMemoryFact
        {
            Id = id,
            Category = category,
            Content = content,
            CreatedAt = "",
            UpdatedAt = ""
        };
    }
}
