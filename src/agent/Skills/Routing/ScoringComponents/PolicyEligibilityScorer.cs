using Hercules.Config;
using Hercules.Storage;

namespace Hercules.Skills.Routing.ScoringComponents;

/// <summary>
///     Policy eligibility scorer.
///     Checks whether the skill's declared permissions and tools are allowed under the current policy.
///     Skills with denied permissions or unregistered required tools are marked ineligible.
/// </summary>
public sealed class PolicyEligibilityScorer : ISkillScorer
{
    private readonly ToolPolicyConfig? _policyConfig;
    private readonly HashSet<string> _allowedPermissions;
    private readonly HashSet<string> _registeredToolNames;

    public PolicyEligibilityScorer(
        ToolPolicyConfig? policyConfig,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> registeredToolNames)
    {
        _policyConfig = policyConfig;
        _allowedPermissions = new HashSet<string>(
            allowedPermissions.Select(p => p.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        _registeredToolNames = new HashSet<string>(
            registeredToolNames.Select(t => t.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public PolicyEligibilityScorer()
    {
        _allowedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _registeredToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public string ComponentName => "policy";

    /// <summary>
    ///     Weight for combining with other scores.
    /// </summary>
    public double Weight { get; set; } = 0.05;

    public ValueTask<ComponentScore?> ScoreAsync(string input, Storage.Skill skill, CancellationToken ct = default)
    {
        var meta = skill.Meta;

        // Check declared permissions
        if (meta.Permissions.Count > 0)
        {
            foreach (var perm in meta.Permissions)
            {
                if (!_allowedPermissions.Contains(perm.ToLowerInvariant()))
                {
                    return new ValueTask<ComponentScore?>(
                        new ComponentScore(
                            ComponentName,
                            Value: 0,
                            IsEligible: false,
                            $"permission '{perm}' not in allowed set"));
                }
            }
        }

        // Check declared tools
        if (meta.Tools is { Count: > 0 })
        {
            foreach (var tool in meta.Tools)
            {
                // If tool is required but not registered → ineligible
                if (tool.Required && !_registeredToolNames.Contains(tool.Name.ToLowerInvariant()))
                {
                    return new ValueTask<ComponentScore?>(
                        new ComponentScore(
                            ComponentName,
                            Value: 0,
                            IsEligible: false,
                            $"required tool '{tool.Name}' not registered"));
                }

                // If tool is in denied list → ineligible
                if (_policyConfig?.DeniedTools.Count > 0)
                {
                    foreach (var pattern in _policyConfig.DeniedTools)
                    {
                        if (MatchesPattern(tool.Name, pattern))
                        {
                            return new ValueTask<ComponentScore?>(
                                new ComponentScore(
                                    ComponentName,
                                    Value: 0,
                                    IsEligible: false,
                                    $"tool '{tool.Name}' matches denied pattern '{pattern}'"));
                        }
                    }
                }
            }
        }

        return new ValueTask<ComponentScore?>(
            new ComponentScore(ComponentName, 1.0, IsEligible: true, "policy checks passed"));
    }

    private static bool MatchesPattern(string toolName, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            return toolName.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith("*"))
            return toolName.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith("*"))
            return toolName.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
        return toolName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
