namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for skill prompt history (Stage 2: the diff view needs a
/// previous version; the agent previously discarded prompt history entirely).
/// </summary>
public sealed class SkillPromptRevisionDto
{
    public required int Version { get; init; }
    public required string Prompt { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
    public required string Source { get; init; }
    public string? Author { get; init; }
}

public sealed class SkillPromptHistoryResponseDto
{
    public required string SkillId { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<SkillPromptRevisionDto> Revisions { get; init; }
}