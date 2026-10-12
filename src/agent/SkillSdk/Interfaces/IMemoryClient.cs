namespace Hercules.SkillSdk;

/// <summary>
///     Safe memory client for file-based skills.
///     Provides read/write access to execution/session/durable/episodic memory layers.
/// </summary>
public interface IMemoryClient
{
    /// <summary>Write a value into memory.</summary>
    Task SetAsync(string key, string value, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default);

    /// <summary>Read a value from memory. Returns null if not found.</summary>
    Task<string?> GetAsync(string key, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default);

    /// <summary>Search memory entries by key prefix or tag.</summary>
    Task<IReadOnlyList<SkillMemoryEntry>> SearchAsync(string? keyPrefix = null, string? tag = null, CancellationToken ct = default);
}
