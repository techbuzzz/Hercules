using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hercules.Mesh;

/// <summary>
///     Факт памяти для синхронизации между доверенными агентами.
///     Только избранные факты (не вся память) — пользователь явно отмечает факты для shared.
/// </summary>
public sealed class SharedMemoryFact
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

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

    private readonly AgentManifestService _manifestService;
    private readonly CapabilityRegistry _registry;
    private readonly IntentTransport _transport;

    public SharedMemorySync(
        string dataRoot,
        CapabilityRegistry registry,
        IntentTransport transport,
        AgentManifestService manifestService)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _manifestService = manifestService ?? throw new ArgumentNullException(nameof(manifestService));

        var sharedDir = Path.Combine(dataRoot, "Memory", "shared");
        Directory.CreateDirectory(sharedDir);
        SharedMemoryPath = Path.Combine(sharedDir, "shared_facts.json");
    }

    /// <summary>Путь к файлу локальных shared-фактов.</summary>
    public string SharedMemoryPath { get; }

    public void Dispose()
    {
    }

    /// <summary>
    ///     Опубликовать факт памяти для синхронизации с доверенными агентами.
    ///     Факт сохраняется локально и отправляется всем доверенным peer'ам.
    /// </summary>
    public async Task<SharedMemoryFact> PublishFactAsync(string category, string content,
        List<string>? allowedAgents = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var fact = new SharedMemoryFact
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Category = category,
            Content = content,
            SourceAgent = _manifestService.Current.AgentId,
            AllowedAgents = allowedAgents ?? new List<string>(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        };

        // Сохраняем локально
        Dictionary<string, SharedMemoryFact> facts = LoadLocalFacts();
        facts[fact.Id] = fact;
        SaveLocalFacts(facts);

        // Отправляем доверенным peer'ам
        await BroadcastFactAsync(fact, ct);

        return fact;
    }

    /// <summary>
    ///     Принять факт от peer-агента (вызывается при получении через intent или API).
    ///     Если факт уже есть и версия новее — обновляем.
    /// </summary>
    public SharedMemoryFact? ReceiveFact(SharedMemoryFact incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        Dictionary<string, SharedMemoryFact> facts = LoadLocalFacts();
        if (facts.TryGetValue(incoming.Id, out SharedMemoryFact? existing))
        {
            if (incoming.Version <= existing.Version)
            {
                return null; // У нас уже более новая версия
            }
        }

        facts[incoming.Id] = incoming;
        SaveLocalFacts(facts);
        return incoming;
    }

    /// <summary>Получить все локальные shared-факты.</summary>
    public List<SharedMemoryFact> GetLocalFacts()
    {
        return LoadLocalFacts().Values.ToList();
    }

    /// <summary>
    ///     Получить факты, доступные указанному агенту (для отправки по запросу).
    ///     Если allowedAgents пуст — факт доступен всем. Иначе — только перечисленным.
    /// </summary>
    public List<SharedMemoryFact> GetFactsForAgent(string targetAgentId)
    {
        return LoadLocalFacts().Values
            .Where(f => f.AllowedAgents.Count == 0 || f.AllowedAgents.Contains(targetAgentId, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    ///     Удалить факт (перестать синхронизировать).
    /// </summary>
    public bool RemoveFact(string factId)
    {
        Dictionary<string, SharedMemoryFact> facts = LoadLocalFacts();
        var removed = facts.Remove(factId);
        if (removed)
        {
            SaveLocalFacts(facts);
        }

        return removed;
    }

    /// <summary>
    ///     Синхронизировать: запросить shared-факты у всех доверенных peer'ов.
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
                // Отправляем запрос на получение shared-фактов
                var envelope = new IntentEnvelope(
                    IntentIds.NewRequestId(),
                    ownAgentId,
                    "shared-memory-fetch",
                    JsonSerializer.Serialize(new { requesterAgentId = ownAgentId }))
                {
                    Deadline = DateTimeOffset.UtcNow.AddMilliseconds(10_000)
                };

                IntentResponse response = await _transport.SendToAsync(peer.AgentId, envelope, ct);
                if (response.IsSuccess && !string.IsNullOrEmpty(response.Result))
                {
                    List<SharedMemoryFact>? peerFacts = JsonSerializer.Deserialize<List<SharedMemoryFact>>(response.Result);
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
            catch
            {
                // Skip peer on error
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

        var envelope = new IntentEnvelope(
            IntentIds.NewRequestId(),
            ownAgentId,
            "shared-memory-push",
            JsonSerializer.Serialize(fact));

        foreach (RegistryAgentEntry peer in peers)
        {
            try
            {
                await _transport.SendToAsync(peer.AgentId, envelope, ct);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    private Dictionary<string, SharedMemoryFact> LoadLocalFacts()
    {
        if (!File.Exists(SharedMemoryPath))
        {
            return new Dictionary<string, SharedMemoryFact>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(SharedMemoryPath);
            List<SharedMemoryFact> list = JsonSerializer.Deserialize<List<SharedMemoryFact>>(json) ?? new List<SharedMemoryFact>();
            return list.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
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
}
