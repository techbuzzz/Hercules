namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for the tool registry.
/// <para>
/// ADR / Stage 0: the agent's OpenAPI document is the input to Studio's generated
/// client. Endpoints returning anonymous objects cannot be described, so
/// <c>GET /api/tools</c> had no response schema at all and full client codegen was
/// impossible. Naming the shape here lets the endpoint declare it via
/// <c>.Produces&lt;T&gt;(200)</c> without changing the serialized JSON.
/// </para>
/// </summary>
/// <para>
/// Non-nullable members use <c>required</c> deliberately: without it the
/// OpenAPI emitter omits them from the schema's <c>required</c> array, every field
/// is generated as optional (<c>string | undefined</c>), and Studio's generated
/// client stops reflecting what the agent actually serialises. Nullable members
/// stay optional, which is correct.
/// </para>
public sealed class ToolSummaryDto
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required bool Enabled { get; init; }
    public required bool Allowed { get; init; }
    public required string RegisteredAt { get; init; }
    public required string Source { get; init; }
    public required bool SupportsHealthCheck { get; init; }
    public required string HealthStatus { get; init; }
    public string? LastCheckedAt { get; init; }
    public string? LastError { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public string? SideEffectLevel { get; init; }
    public string? RequiredPermissions { get; init; }
    public required int TimeoutSeconds { get; init; }
    public ToolLimitsDto? Limits { get; init; }
}

public sealed class ToolLimitsDto
{
    public required int MaxCallsPerMinute { get; init; }
    public required int TimeoutSeconds { get; init; }
}

public sealed class ToolsListResponseDto
{
    public required int Count { get; init; }
    public required int AllowedCount { get; init; }
    public required IReadOnlyList<ToolSummaryDto> Tools { get; init; }
}