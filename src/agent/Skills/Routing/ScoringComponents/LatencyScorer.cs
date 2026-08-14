using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Latency scorer.
///     Prefers skills with lower historical average latency.
///     Uses a sigmoid-like mapping: latency_ms → score [0..1], where lower latency = higher score.
///     Score = 1 / (1 + (latency / referenceLatency)^2) — decays smoothly.
/// </summary>
public sealed class LatencyScorer : ISkillScorer
{
    /// <summary>
    ///     Reference latency in ms: skills at this latency get score=0.5.
    /// </summary>
    private readonly double _referenceLatencyMs;

    /// <summary>
    ///     Skill ID → list of latency measurements (ms). Set by SkillScoringEngine after scoring.
    /// </summary>
    private readonly Dictionary<string, List<double>> _skillLatencies = new(StringComparer.OrdinalIgnoreCase);

    public LatencyScorer(double referenceLatencyMs = 500)
    {
        _referenceLatencyMs = referenceLatencyMs;
    }

    public string ComponentName => "latency";

    /// <summary>
    ///     Weight for combining with other scores.
    /// </summary>
    public double Weight { get; set; } = 0.05;

    /// <summary>
    ///     Set latency history for a skill (called by SkillScoringEngine or storage service).
    /// </summary>
    public void SetLatencyHistory(string skillId, IEnumerable<double> latenciesMs)
    {
        _skillLatencies[skillId] = latenciesMs.ToList();
    }

    public ValueTask<ComponentScore?> ScoreAsync(string input, Storage.Skill skill, CancellationToken ct = default)
    {
        if (!_skillLatencies.TryGetValue(skill.Meta.Id, out var latencies) || latencies.Count == 0)
        {
            // No latency data: neutral score (skill is neither penalized nor rewarded)
            return new ValueTask<ComponentScore?>(
                new ComponentScore(ComponentName, 1.0, IsEligible: true, "no latency history (neutral)"));
        }

        var avgLatency = latencies.Average();
        // Sigmoid-like: lower latency → higher score
        var ratio = avgLatency / _referenceLatencyMs;
        var score = 1.0 / (1.0 + ratio * ratio);

        return new ValueTask<ComponentScore?>(
            new ComponentScore(
                ComponentName,
                score,
                IsEligible: true,
                $"avgLatency={avgLatency:F0}ms, ref={_referenceLatencyMs:F0}ms"));
    }
}
