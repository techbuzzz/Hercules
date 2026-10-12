namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for the mesh routing query (Stage 6.4).
/// <para>
/// <c>GET /api/mesh/router/routes</c> answers "which peers would serve capability X,
/// and on what basis" — the scores, trust verdict and circuit state that produced the
/// ranking. Surfaced in Studio as a routing inspector.
/// </para>
/// </summary>
public sealed class MeshRouteCandidateDto
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public required string Endpoint { get; init; }
    public required double HealthScore { get; init; }
    public required double LatencyMs { get; init; }
    public required double QualityScore { get; init; }
    public required string TrustLevel { get; init; }
    public required double CompositeScore { get; init; }
    public required decimal CostHintUsd { get; init; }

    /// <summary>Rendered from the <c>CircuitState</c> enum so the document stays schema-able.</summary>
    public required string CircuitState { get; init; }

    public DateTimeOffset? LastSeen { get; init; }
    public required bool TrustPassed { get; init; }
}

public sealed class MeshRoutesResponseDto
{
    public required string Capability { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<MeshRouteCandidateDto> Candidates { get; init; }
}