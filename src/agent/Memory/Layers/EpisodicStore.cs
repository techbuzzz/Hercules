using System.Text.Json;
using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Memory.Layers;

/// <summary>
///     Append-only episodic store. Backed by Memory/Episodes/{yyyy-MM}.md
///     Each month is one file with YAML-frontmatter-style headers per episode.
/// </summary>
public sealed class EpisodicStore : IEpisodicStore
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public EpisodicStore(StorageConfig storageConfig)
    {
        _dir = Path.Combine(storageConfig.DataRoot, storageConfig.MemoryDir, Hercules.BuiltIn.EpisodesSubdir);
        Directory.CreateDirectory(_dir);
    }

    public async Task AppendEpisodeAsync(string sessionId, string summary, MemoryEntry entry, CancellationToken ct = default)
    {
        var monthFile = Path.Combine(_dir, $"{DateTime.UtcNow:yyyy-MM}.md");
        var timestamp = DateTime.UtcNow.ToString("O");
        var tags = string.Join(", ", entry.Tags);
        var frontmatter = $"""
            ### Episode | {timestamp} | session:{sessionId} | source:{entry.Source} | confidence:{entry.Confidence} | sensitivity:{entry.Sensitivity} | tags:[{tags}]

            {summary.Trim()}

            """;

        await File.AppendAllTextAsync(monthFile, frontmatter, ct);
    }

    public async Task<IReadOnlyList<Episode>> GetRecentEpisodesAsync(int count = 5, CancellationToken ct = default)
    {
        var episodes = new List<Episode>();

        foreach (var file in Directory.EnumerateFiles(_dir, "*.md").OrderByDescending(f => f).Take(3))
        {
            ct.ThrowIfCancellationRequested();
            episodes.AddRange(ParseEpisodes(await File.ReadAllTextAsync(file, ct)));
        }

        return episodes
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList();
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesBySessionAsync(string sessionId, CancellationToken ct = default)
    {
        var episodes = new List<Episode>();

        foreach (var file in Directory.EnumerateFiles(_dir, "*.md"))
        {
            ct.ThrowIfCancellationRequested();
            var fileEpisodes = ParseEpisodes(await File.ReadAllTextAsync(file, ct));
            episodes.AddRange(fileEpisodes.Where(e => e.SessionId == sessionId));
        }

        return episodes.OrderByDescending(e => e.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Episode>> SearchEpisodesAsync(string query, int maxCount = 10, CancellationToken ct = default)
    {
        var episodes = new List<Episode>();

        foreach (var file in Directory.EnumerateFiles(_dir, "*.md"))
        {
            ct.ThrowIfCancellationRequested();
            var fileContent = await File.ReadAllTextAsync(file, ct);
            if (!fileContent.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            episodes.AddRange(ParseEpisodes(fileContent));
        }

        return episodes
            .Where(e => e.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .Take(maxCount)
            .ToList();
    }

    private static IEnumerable<Episode> ParseEpisodes(string content)
    {
        // Parse "### Episode | {timestamp} | session:{sessionId} | ..." blocks
        var blocks = content.Split("### Episode", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var headerEnd = trimmed.IndexOf('\n');
            if (headerEnd < 0) continue;

            var header = trimmed[..headerEnd];
            var summary = trimmed[(headerEnd + 1)..].Trim();

            // Parse header: "Episode | {timestamp} | session:{sessionId} | ..."
            var sessionId = Extract(header, "session:");
            var source = Extract(header, "source:");
            var confidence = Extract(header, "confidence:");
            var sensitivity = Extract(header, "sensitivity:");
            var tagsStr = Extract(header, "tags:[")?.TrimEnd(']') ?? "";

            if (string.IsNullOrEmpty(sessionId)) continue;

            DateTime createdAt = DateTime.TryParse(Extract(header, "Episode |")?.Split('|')[0]?.Trim(), out var dt)
                ? dt
                : DateTime.MinValue;

            var entry = new MemoryEntry(
                source ?? "episodic",
                Enum.TryParse<MemoryConfidence>(confidence, out var conf) ? conf : MemoryConfidence.Medium,
                0,
                Enum.TryParse<MemorySensitivity>(sensitivity, out var sens) ? sens : MemorySensitivity.Internal,
                createdAt,
                tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList());

            yield return new Episode(sessionId!, summary, entry, createdAt);
        }
    }

    private static string? Extract(string header, string prefix)
    {
        var idx = header.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        var valueStart = idx + prefix.Length;
        var valueEnd = header.IndexOf('|', valueStart);
        return valueEnd < 0
            ? header[valueStart..].Trim()
            : header[valueStart..valueEnd].Trim();
    }
}
