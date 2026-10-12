namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shape for <c>GET /api/config</c>.
/// <para>
/// The endpoint returns an anonymous object, so the OpenAPI document described
/// nothing and Studio had to hand-write <c>ConfigDto</c>. The live configuration
/// itself is <c>RuntimeConfigStore.Current</c>, whose keys are open-ended — it is
/// exposed as a JSON object rather than a closed set of members.
/// </para>
/// </summary>
public sealed class AgentConfigDto
{
    /// <summary>The live configuration. Open-ended by design.</summary>
    public required IReadOnlyDictionary<string, object?> Config { get; init; }

    /// <summary>Where the configuration came from (currently always "runtime").</summary>
    public required string Source { get; init; }
}

/// <summary>
/// Response shape for <c>PATCH</c>/<c>PUT /api/config</c>.
/// </summary>
public sealed class ConfigUpdateResponseDto
{
    public required string Status { get; init; }
    public required IReadOnlyDictionary<string, object?> Config { get; init; }
}