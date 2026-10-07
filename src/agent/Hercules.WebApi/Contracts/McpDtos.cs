namespace Hercules.WebApi.Contracts;

/// <summary>
///     The *configured* shape of one MCP server, mirroring <c>McpServerConfig</c>.
///     Studio edits this and writes it back through <c>PATCH /api/config</c>, which
///     triggers <c>McpClientService</c>'s diff-driven hot-reload — there is no
///     dedicated add/remove endpoint because the config store is the write surface.
/// </summary>
public sealed class McpServerDefinitionDto
{
    public required string Name { get; init; }
    public required string Transport { get; init; }
    public string? Command { get; init; }

    /// <summary>Process arguments for <c>stdio</c> transport. Empty for http/sse.</summary>
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    /// <summary>Absolute URL for <c>http</c>/<c>sse</c> transport. Null for <c>stdio</c>.</summary>
    public string? Endpoint { get; init; }

    public required bool Enabled { get; init; }
    public required bool HealthCheckEnabled { get; init; }
    public required int TimeoutSeconds { get; init; }
}

/// <summary>
/// Response shapes for the MCP endpoints (Stage 0: named so Studio's generated
/// client can describe them; the endpoints previously returned anonymous objects
/// and produced no schema).
///
/// Stage 5b additionally carries <see cref="Config"/> so the editor can round-trip
/// a server without a second, separately-typed read endpoint.
/// </summary>
public sealed class McpServerSummaryDto
{
    public required string Name { get; init; }
    public required string Transport { get; init; }
    public required string Status { get; init; }
    public required int ToolCount { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public string? Error { get; init; }

    /// <summary>Live configuration, round-trippable into <c>PATCH /api/config</c>.</summary>
    public required McpServerDefinitionDto Config { get; init; }
}

public sealed class McpServersListResponseDto
{
    public required int Count { get; init; }
    public required IReadOnlyList<McpServerSummaryDto> Servers { get; init; }
}

public sealed class McpServerDetailDto
{
    public required string Name { get; init; }
    public required string Transport { get; init; }
    public required string Status { get; init; }
    public required int ToolCount { get; init; }
    public string? ServerVersion { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public string? Error { get; init; }

    /// <summary>Live configuration, round-trippable into <c>PATCH /api/config</c>.</summary>
    public required McpServerDefinitionDto Config { get; init; }
}

public sealed class McpReloadResponseDto
{
    public required string Message { get; init; }
}