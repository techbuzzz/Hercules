using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hercules.WebApi.Skills;

/// <summary>One recorded revision of a skill prompt.</summary>
public sealed record SkillPromptRevision
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
    [JsonPropertyName("changedAt")] public DateTimeOffset ChangedAt { get; init; }
    [JsonPropertyName("source")] public string Source { get; init; } = "manual";
    [JsonPropertyName("author")] public string? Author { get; init; }
}

/// <summary>
/// Append-only history of skill prompt revisions.
/// <para>
/// Stage 2: Studio's skill editor needs a diff, and a diff needs a previous
/// version. The agent kept quality and eval history but discarded prompt
/// history — an edit was destructive. This store closes that gap.
/// </para>
/// <para>
/// Storage is one small JSON file per skill under the data root. Bounded to
/// <see cref="MaxRevisionsPerSkill"/> newest entries so a frequently edited
/// skill cannot grow without limit.
/// </para>
/// </summary>
public sealed class SkillPromptHistoryStore
{
    /// <summary>Revisions retained per skill; older ones are dropped on append.</summary>
    public const int MaxRevisionsPerSkill = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly ILogger<SkillPromptHistoryStore> _logger;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, List<SkillPromptRevision>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _dir;

    public SkillPromptHistoryStore(
        ILogger<SkillPromptHistoryStore> logger,
        string historyDirectory,
        TimeProvider? clock = null)
    {
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _dir = historyDirectory;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Records a revision. A no-op when the prompt is unchanged.</summary>
    public async Task RecordAsync(string skillId, int version, string prompt, string source, string? author = null)
    {
        if (string.IsNullOrWhiteSpace(skillId) || prompt is null) return;

        await _gate.WaitAsync();
        try
        {
            var list = Load(skillId);
            if (list.Count > 0 && string.Equals(list[^1].Prompt, prompt, StringComparison.Ordinal)) return;

            list.Add(new SkillPromptRevision
            {
                Version = version,
                Prompt = prompt,
                ChangedAt = _clock.GetUtcNow(),
                Source = source,
                Author = author,
            });

            if (list.Count > MaxRevisionsPerSkill)
            {
                list.RemoveRange(0, list.Count - MaxRevisionsPerSkill);
            }

            _cache[skillId] = list;
            await SaveAsync(skillId, list);
        }
        catch (Exception ex)
        {
            // History is an audit aid, never a reason to fail an edit.
            _logger.LogWarning(ex, "[SkillPromptHistory] Could not record revision for {SkillId}", skillId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Revisions oldest-first. Empty when the skill has no history yet.</summary>
    public IReadOnlyList<SkillPromptRevision> Get(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return [];
        lock (_cache)
        {
            return _cache.TryGetValue(skillId, out var cached) ? cached.ToArray() : [];
        }
    }

    private List<SkillPromptRevision> Load(string skillId)
    {
        if (_cache.TryGetValue(skillId, out var cached)) return cached;

        var list = new List<SkillPromptRevision>();
        var path = PathFor(skillId);
        if (File.Exists(path))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<SkillPromptRevision>>(File.ReadAllText(path), Json);
                if (parsed is not null) list.AddRange(parsed);
            }
            catch (Exception ex)
            {
                // Corrupt history must not break reads; start a fresh chain.
                _logger.LogWarning(ex, "[SkillPromptHistory] Discarding unreadable history for {SkillId}", skillId);
            }
        }

        _cache[skillId] = list;
        return list;
    }

    private async Task SaveAsync(string skillId, List<SkillPromptRevision> list)
    {
        var path = PathFor(skillId);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(list, Json));
        // Atomic replace: a crash mid-write must not leave a truncated file.
        File.Move(tmp, path, overwrite: true);
    }

    private string PathFor(string skillId)
    {
        var safe = string.Concat(skillId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_dir, $"{safe}.json");
    }
}