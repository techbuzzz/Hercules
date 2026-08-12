namespace Hercules.Memory.Layers;

/// <summary>
///     Append-only episodic record store backed by Markdown files.
///     Stores session summaries with metadata for retrieval.
/// </summary>
public interface IEpisodicStore
{
    /// <summary>Append a new episode (session summary). Appends only — never overwrites.</summary>
    Task AppendEpisodeAsync(string sessionId, string summary, MemoryEntry entry, CancellationToken ct = default);

    /// <summary>Get the most recent N episodes.</summary>
    Task<IReadOnlyList<Episode>> GetRecentEpisodesAsync(int count = 5, CancellationToken ct = default);

    /// <summary>Get all episodes for a specific session.</summary>
    Task<IReadOnlyList<Episode>> GetEpisodesBySessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Search episodes by text query (simple contains).</summary>
    Task<IReadOnlyList<Episode>> SearchEpisodesAsync(string query, int maxCount = 10, CancellationToken ct = default);
}

public sealed record Episode(
    string SessionId,
    string Summary,
    MemoryEntry Entry,
    DateTime CreatedAt);
