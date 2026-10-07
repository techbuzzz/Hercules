using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Memory.Layers;

/// <summary>
///     Long-lived durable fact store. Each fact is a Markdown file with a JSON sidecar.
///     Directory: Memory/DurableFacts/{slug(key)}.md + .meta.json
/// </summary>
public sealed class DurableFactsService : IDurableFactsStore
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private static readonly char[] _invalidFileChars = Path.GetInvalidFileNameChars();

    public DurableFactsService(StorageConfig storageConfig)
    {
        _dir = Path.Combine(storageConfig.DataRoot, storageConfig.MemoryDir, Hercules.BuiltIn.DurableFactsSubdir);
        Directory.CreateDirectory(_dir);
    }

    public async Task StoreFactAsync(string key, string value, MemoryEntry entry, CancellationToken ct = default)
    {
        var safeKey = Slugify(key);
        var mdPath = Path.Combine(_dir, $"{safeKey}.md");
        var metaPath = Path.Combine(_dir, $"{safeKey}.meta.json");

        await File.WriteAllTextAsync(mdPath, value.Trim() + "\n", ct);

        var meta = new MemoryEntryMetadata
        {
            Key = key,
            Sensitivity = entry.Sensitivity.ToString(),
            Source = entry.Source,
            Confidence = entry.Confidence.ToString(),
            TtlMinutes = entry.TtlMinutes,
            Tags = entry.Tags,
            CreatedAt = entry.CreatedAt
        };
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, _jsonOpts), ct);
    }

    public async Task<(string? Value, MemoryEntry? Entry)?> GetFactAsync(string key, CancellationToken ct = default)
    {
        var safeKey = Slugify(key);
        var mdPath = Path.Combine(_dir, $"{safeKey}.md");
        var metaPath = Path.Combine(_dir, $"{safeKey}.meta.json");

        if (!File.Exists(mdPath))
        {
            return null;
        }

        var value = await File.ReadAllTextAsync(mdPath, ct);
        MemoryEntry entry;

        if (File.Exists(metaPath))
        {
            var metaJson = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<MemoryEntryMetadata>(metaJson);
            entry = meta is not null
                ? new MemoryEntry(
                    meta.Source,
                    Enum.TryParse<MemoryConfidence>(meta.Confidence, out var conf) ? conf : MemoryConfidence.Medium,
                    meta.TtlMinutes,
                    Enum.TryParse<MemorySensitivity>(meta.Sensitivity, out var sens) ? sens : MemorySensitivity.Internal,
                    meta.CreatedAt,
                    meta.Tags ?? new List<string>())
                : new MemoryEntry("unknown");
        }
        else
        {
            entry = new MemoryEntry("legacy", MemoryConfidence.Medium);
        }

        return (value.Trim(), entry);
    }

    public Task<bool> DeleteFactAsync(string key, CancellationToken ct = default)
    {
        var safeKey = Slugify(key);
        var mdPath = Path.Combine(_dir, $"{safeKey}.md");
        var metaPath = Path.Combine(_dir, $"{safeKey}.meta.json");
        var deleted = false;

        if (File.Exists(mdPath))
        {
            File.Delete(mdPath);
            deleted = true;
        }

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.FromResult(deleted);
    }

    public Task<IReadOnlyList<string>> ListFactKeysAsync(CancellationToken ct = default)
    {
        var keys = Directory.EnumerateFiles(_dir, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(f => !f.EndsWith(".meta"))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public async Task<IReadOnlyList<(string Key, string Value, MemoryEntry Entry)>> SearchFactsAsync(
        string? tag = null,
        string? keyPrefix = null,
        bool includeExpired = false,
        CancellationToken ct = default)
    {
        var results = new List<(string Key, string Value, MemoryEntry Entry)>();

        foreach (var mdPath in Directory.EnumerateFiles(_dir, "*.md"))
        {
            ct.ThrowIfCancellationRequested();
            var safeKey = Path.GetFileNameWithoutExtension(mdPath);
            var metaPath = Path.Combine(_dir, $"{safeKey}.meta.json");

            if (!File.Exists(metaPath))
            {
                continue;
            }

            var metaJson = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<MemoryEntryMetadata>(metaJson);
            if (meta is null) continue;

            var entry = new MemoryEntry(
                meta.Source,
                Enum.TryParse<MemoryConfidence>(meta.Confidence, out var conf) ? conf : MemoryConfidence.Medium,
                meta.TtlMinutes,
                Enum.TryParse<MemorySensitivity>(meta.Sensitivity, out var sens) ? sens : MemorySensitivity.Internal,
                meta.CreatedAt,
                meta.Tags ?? new List<string>());

            if (!includeExpired && entry.IsExpired)
            {
                continue;
            }

            if (tag is not null && !entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (keyPrefix is not null && !safeKey.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = await File.ReadAllTextAsync(mdPath, ct);
            results.Add((meta.Key ?? safeKey, value.Trim(), entry));
        }

        return results;
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        int removed = 0;
        foreach (var metaPath in Directory.EnumerateFiles(_dir, "*.meta.json"))
        {
            ct.ThrowIfCancellationRequested();
            var metaJson = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<MemoryEntryMetadata>(metaJson);
            if (meta is null) continue;

            if (meta.TtlMinutes > 0 && DateTime.UtcNow > meta.CreatedAt.AddMinutes(meta.TtlMinutes))
            {
                var mdPath = Path.ChangeExtension(metaPath, ".md");
                if (File.Exists(mdPath)) File.Delete(mdPath);
                File.Delete(metaPath);
                removed++;
            }
        }

        return removed;
    }

    private static string Slugify(string key)
    {
        var slug = new string(key
            .ToLowerInvariant()
            .Select(c => _invalidFileChars.Contains(c) ? '_' : c)
            .ToArray());

        return slug.Length > 80 ? slug[..80] : slug;
    }
}
