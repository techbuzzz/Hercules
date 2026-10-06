namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for the Studio endpoints added by ADR-0009.
/// <para>
/// These were written after the codegen pipeline existed, so they are named from
/// the start: an undeclared response shape is exactly the drift that made
/// Studio's client hand-written and wrong.
/// </para>
/// </summary>
public sealed class StudioSessionResponseDto
{
    public required string Token { get; init; }
    public required string Role { get; init; }
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required int TtlSeconds { get; init; }
}

public sealed class StartCodeRunResponseDto
{
    public required string RunId { get; init; }
}

public sealed class CodeRunResultDto
{
    public required int ExitCode { get; init; }
    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
    public required long DurationMs { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<string> BlockedPatterns { get; init; }
    public string? SessionDir { get; init; }
    public required bool Success { get; init; }
}

public sealed class CodeRunStatusDto
{
    public required string RunId { get; init; }
    public string? SkillId { get; init; }
    public required string Language { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Error { get; init; }
    public CodeRunResultDto? Result { get; init; }
}