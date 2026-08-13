namespace Hercules.Mesh.Router;

/// <summary>
///     Composite scoring for peer candidates.
///     Formula: w_health*health + w_latency*latency + w_quality*quality + w_trust*trust
///     All components are normalised to [0, 1] before weighting.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_043 § RouterRanking.
/// </summary>
public static class RouterRanking
{
    // Latency bounds used for normalisation (ms)
    private const double MinLatencyMs = 10;
    private const double MaxLatencyMs = 10_000;

    /// <summary>
    ///     Compute the composite routing score for a candidate.
    /// </summary>
    /// <param name="candidate">The peer candidate to score.</param>
    /// <param name="weights">Component weights.</param>
    /// <returns>Composite score in [0, 1]; higher is better.</returns>
    public static double ComputeScore(PeerCandidate candidate, RouterWeights weights)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(weights);

        double healthNorm = Clamp(candidate.HealthScore, 0, 1);
        double latencyNorm = LatencyScore(candidate.LatencyMs);
        double qualityNorm = Clamp(candidate.QualityScore, 0, 1);
        double trustNorm = candidate.TrustScore;

        double total = weights.Health + weights.Latency + weights.Quality + weights.Trust;
        if (total <= 0)
        {
            return 0;
        }

        double score = (
            (weights.Health / total) * healthNorm +
            (weights.Latency / total) * latencyNorm +
            (weights.Quality / total) * qualityNorm +
            (weights.Trust / total) * trustNorm);

        return Math.Round(Clamp(score, 0, 1), 4);
    }

    /// <summary>
    ///     Normalise latency to [0, 1] where 0 ms → 1.0 and MaxLatencyMs → 0.0.
    /// </summary>
    public static double LatencyScore(int latencyMs)
    {
        if (latencyMs <= 0)
        {
            return 1.0;
        }

        double raw = 1.0 - ((latencyMs - MinLatencyMs) / (MaxLatencyMs - MinLatencyMs));
        return Clamp(raw, 0, 1);
    }

    /// <summary>
    ///     Map a trust level string to a normalised score [0, 1].
    /// </summary>
    public static double NormaliseTrust(string trustLevel)
    {
        return trustLevel.ToLowerInvariant() switch
        {
            "verified" => 1.0,
            "trusted" => 0.9,
            "provisionally-trusted" => 0.7,
            "unverified" => 0.5,
            "suspicious" => 0.2,
            "denied" => 0.0,
            _ => 0.5
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
