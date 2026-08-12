using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Input-schema compatibility scorer.
///     Checks if the skill declares required tools or input schema that can be satisfied.
///     A skill is incompatible (IsEligible=false) only when it explicitly requires a tool
///     or schema version that is not registered or not supported.
///     Default: compatible (score=1, eligible=true).
/// </summary>
public sealed class SchemaCompatibilityScorer : ISkillScorer
{
    private readonly HashSet<string> _registeredToolNames;

    public SchemaCompatibilityScorer(IEnumerable<string> registeredToolNames)
    {
        _registeredToolNames = new HashSet<string>(
            registeredToolNames.Select(t => t.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public SchemaCompatibilityScorer()
    {
        _registeredToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public string ComponentName => "schema";

    /// <summary>
    ///     Weight for combining with other scores.
    /// </summary>
    public double Weight { get; set; } = 0.15;

    public ValueTask<ComponentScore?> ScoreAsync(string input, Storage.Skill skill, CancellationToken ct = default)
    {
        // Check declared tools compatibility
        var tools = skill.Meta.Tools;
        if (tools is { Count: > 0 })
        {
            foreach (var decl in tools)
            {
                // If the tool is marked as required but not registered → ineligible
                if (decl.Required && !_registeredToolNames.Contains(decl.Name.ToLowerInvariant()))
                {
                    return new ValueTask<ComponentScore?>(
                        new ComponentScore(
                            ComponentName,
                            Value: 0,
                            IsEligible: false,
                            $"required tool '{decl.Name}' not registered"));
                }
            }
        }

        // Skill is compatible; score based on how well-defined its schema is
        // A skill with declared tools gets a higher base score
        var baseScore = tools is { Count: > 0 } ? 1.0 : 0.8;

        return new ValueTask<ComponentScore?>(
            new ComponentScore(
                ComponentName,
                baseScore,
                IsEligible: true,
                tools is { Count: > 0 }
                    ? $"{tools.Count} declared tool(s)"
                    : "no declared tools"));
    }
}
