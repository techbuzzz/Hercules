using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Policy;

/// <summary>
///     Implementation of <see cref="ITrustAdmissionPolicy"/>.
///     Evaluates inter-agent requests against trust level, intent allow-lists,
///     data classification, schema version, and resource budget constraints.
///
///     Evaluation order:
///     1. Policy disabled → allow
///     2. Dry-run mode → log and return Allowed(dryRun: true)
///     3. Trust level check → deny if below minimum
///     4. Intent allow-list → deny if intent not in list (if list is non-empty)
///     5. Data classification → deny if exceeds allowed max
///     6. Schema version → deny if mismatch and mismatch not allowed
///     7. Resource budget → deny if request exceeds target limits
///     8. Risk level → deny if risk exceeds allowed threshold
///
///     Specification: docs/ROADMAP-RU.md Phase 3 task_040.
/// </summary>
public sealed class TrustAdmissionPolicyEngine : ITrustAdmissionPolicy
{
    private readonly TrustAdmissionConfig _config;
    private readonly ILogger<TrustAdmissionPolicyEngine> _logger;
    private readonly PolicyMode _effectiveMode;

    public TrustAdmissionPolicyEngine(TrustAdmissionConfig config, ILogger<TrustAdmissionPolicyEngine> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _effectiveMode = ParseMode(_config.PolicyMode);
    }

    private static PolicyMode ParseMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "enforce" => PolicyMode.Enforce,
            "dryrun" => PolicyMode.DryRun,
            "disabled" => PolicyMode.Disabled,
            _ => PolicyMode.DryRun // Default to DryRun for safety
        };
    }

    /// <inheritdoc />
    public PolicyMode Mode
    {
        get
        {
            if (!_config.Enabled) return PolicyMode.Disabled;
            return _effectiveMode;
        }
    }

    /// <inheritdoc />
    public TrustAdmissionResult Evaluate(TrustAdmissionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // 1. Policy disabled (Either via Enabled=false or Mode=Disabled)
        if (!_config.Enabled || Mode == PolicyMode.Disabled)
        {
            _logger.LogDebug("[TrustPolicy] Policy disabled — allowing request {RequestId} from {Sender}",
                ctx.Envelope.RequestId, ctx.Envelope.Sender);
            return TrustAdmissionResult.Allowed();
        }

        // 2. Dry-run: evaluate but don't block
        bool dryRun = Mode == PolicyMode.DryRun;
        if (dryRun)
        {
            _logger.LogDebug("[TrustPolicy:DryRun] Evaluating request {RequestId} from {Sender}",
                ctx.Envelope.RequestId, ctx.Envelope.Sender);
        }

        // 3. Trust level check
        if (_config.AllowedTrustLevels.Count > 0)
        {
            var callerLevel = ctx.CallerTrustLevel;
            if (!IsLevelAllowed(callerLevel))
            {
                string reason = $"Trust level '{callerLevel}' is below minimum required " +
                                $"({string.Join(", ", _config.AllowedTrustLevels.Select(l => l.ToString()))}) " +
                                $"for caller '{ctx.Envelope.Sender}'";
                _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                    ctx.Envelope.RequestId, reason);
                return TrustAdmissionResult.Denied(reason, TrustDenialReason.TrustLevelTooLow, dryRun);
            }
        }

        // 4. Intent allow-list
        if (_config.AllowedIntents.Count > 0)
        {
            var intent = ctx.Envelope.Intent;
            if (!string.IsNullOrEmpty(intent) &&
                !_config.AllowedIntents.Contains(intent, StringComparer.OrdinalIgnoreCase))
            {
                string reason = $"Intent '{intent}' is not in the allowed-intent list for target '{ctx.TargetAgentId}'";
                _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                    ctx.Envelope.RequestId, reason);
                return TrustAdmissionResult.Denied(reason, TrustDenialReason.IntentNotAllowed, dryRun);
            }
        }

        // 5. Data classification
        if (_config.AllowedClassifications.Count > 0)
        {
            var classification = ctx.DataClassification;
            var parsedClassifications = _config.AllowedClassifications
                .Select(c => Enum.TryParse<DataClassification>(c, true, out var v) ? v : DataClassification.Public)
                .ToList();

            if (parsedClassifications.Count > 0)
            {
                var maxAllowed = parsedClassifications.Max();
                if (classification > maxAllowed)
                {
                    string reason = $"Data classification '{classification}' exceeds allowed maximum '{maxAllowed}' " +
                                    $"for target '{ctx.TargetAgentId}'";
                    _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                        ctx.Envelope.RequestId, reason);
                    return TrustAdmissionResult.Denied(reason, TrustDenialReason.ClassificationTooHigh, dryRun);
                }
            }
        }

        // 6. Schema version compatibility
        if (!_config.AllowSchemaMismatch &&
            !string.IsNullOrEmpty(ctx.TargetMinSchemaVersion) &&
            !string.IsNullOrEmpty(ctx.CallerSchemaVersion))
        {
            if (!IsSchemaCompatible(ctx.CallerSchemaVersion, ctx.TargetMinSchemaVersion))
            {
                string reason = $"Schema version mismatch: caller supports '{ctx.CallerSchemaVersion}', " +
                                $"target requires ≥ '{ctx.TargetMinSchemaVersion}'";
                _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                    ctx.Envelope.RequestId, reason);
                return TrustAdmissionResult.Denied(reason, TrustDenialReason.SchemaVersionMismatch, dryRun);
            }
        }

        // 7. Resource budget / limits
        if (!_config.AllowBudgetExceeded && ctx.TargetResourceLimits is not null)
        {
            var limits = ctx.TargetResourceLimits;
            var reason = CheckBudgetExceeded(ctx.Envelope, limits);
            if (reason is not null)
            {
                _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                    ctx.Envelope.RequestId, reason);
                return TrustAdmissionResult.Denied(reason, TrustDenialReason.BudgetLimitExceeded, dryRun);
            }
        }

        // 8. Risk level
        if (_config.AllowedRiskLevels.Count > 0 && !string.IsNullOrEmpty(ctx.RequestedRiskLevel))
        {
            if (!_config.AllowedRiskLevels.Contains(ctx.RequestedRiskLevel, StringComparer.OrdinalIgnoreCase))
            {
                string reason = $"Risk level '{ctx.RequestedRiskLevel}' is not in the allowed list " +
                                $"for intent '{ctx.Envelope.Intent}'";
                _logger.LogWarning("[TrustPolicy] {RequestId} DENIED: {Reason}",
                    ctx.Envelope.RequestId, reason);
                return TrustAdmissionResult.Denied(reason, TrustDenialReason.RiskLevelMismatch, dryRun);
            }
        }

        _logger.LogDebug("[TrustPolicy] {RequestId} ALLOWED (trustLevel={TrustLevel}, intent={Intent})",
            ctx.Envelope.RequestId, ctx.CallerTrustLevel, ctx.Envelope.Intent);
        return TrustAdmissionResult.Allowed(dryRun);
    }

    /// <summary>
    ///     Check if the caller's trust level is in the allowed list.
    ///     Parses string config values to TrustLevel enum and uses >= comparison.
    /// </summary>
    private bool IsLevelAllowed(TrustLevel level)
    {
        if (_config.AllowedTrustLevels.Count == 0) return true;

        // Parse all configured levels to enum and find minimum
        var parsedLevels = _config.AllowedTrustLevels
            .Select(l => Enum.TryParse<TrustLevel>(l, true, out var v) ? v : TrustLevel.Unverified)
            .ToList();

        if (parsedLevels.Count == 0) return true;

        var minAllowed = parsedLevels.Min();
        return level >= minAllowed;
    }

    /// <summary>
    ///     Simple schema version compatibility check.
    ///     Caller version must be >= target minimum (major.minor format).
    /// </summary>
    private static bool IsSchemaCompatible(string callerVersion, string targetMinVersion)
    {
        if (string.IsNullOrEmpty(callerVersion) || string.IsNullOrEmpty(targetMinVersion))
            return true;

        try
        {
            var callerParts = callerVersion.TrimStart('v', 'V').Split('.');
            var targetParts = targetMinVersion.TrimStart('v', 'V').Split('.');

            int callerMajor = int.Parse(callerParts[0]);
            int targetMajor = int.Parse(targetParts[0]);

            if (callerMajor > targetMajor) return true;
            if (callerMajor < targetMajor) return false;

            // Same major — check minor
            int callerMinor = callerParts.Length > 1 ? int.Parse(callerParts[1]) : 0;
            int targetMinor = targetParts.Length > 1 ? int.Parse(targetParts[1]) : 0;
            return callerMinor >= targetMinor;
        }
        catch
        {
            // Fallback: string comparison if parsing fails
            return string.Compare(callerVersion, targetMinVersion, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    ///     Check if the envelope exceeds the target's declared resource limits.
    ///     Returns null if within limits, or a denial message otherwise.
    /// </summary>
    private static string? CheckBudgetExceeded(IntentEnvelope envelope, ManifestResourceLimits limits)
    {
        // If the envelope carries declared limits, compare them
        // (we use ManifestResourceLimits.MaxTokensPerRequest as a proxy for input token count)
        if (limits.MaxTokensPerRequest > 0)
        {
            // Approximate: payload length in characters / 4 ≈ token count
            int estimatedTokens = envelope.Payload?.Length / 4 ?? 0;
            if (estimatedTokens > limits.MaxTokensPerRequest)
            {
                return $"Estimated input tokens ({estimatedTokens}) exceeds target limit ({limits.MaxTokensPerRequest})";
            }
        }

        if (limits.MaxWallClockSecondsPerRequest > 0 && envelope.Deadline.HasValue)
        {
            var deadline = envelope.Deadline.Value;
            var timeout = deadline - DateTimeOffset.UtcNow;
            if (timeout.TotalSeconds > limits.MaxWallClockSecondsPerRequest)
            {
                return $"Requested wall-clock time ({timeout.TotalSeconds:F0}s) exceeds target limit ({limits.MaxWallClockSecondsPerRequest}s)";
            }
        }

        return null;
    }
}
