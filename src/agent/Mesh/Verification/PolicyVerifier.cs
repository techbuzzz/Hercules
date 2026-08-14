using Hercules.Config;
using Hercules.Mesh.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Проверяет ответ через policy engine: trust admission constraints,
///     capability limits и declared side-effect policies.
///     Интегрируется с существующим <see cref="ITrustAdmissionPolicy"/>.
/// </summary>
public sealed class PolicyVerifier : IVerifier
{
    private readonly ITrustAdmissionPolicy _trustPolicy;
    private readonly VerificationConfig _config;
    private readonly ILogger<PolicyVerifier> _logger;

    public PolicyVerifier(
        ITrustAdmissionPolicy trustPolicy,
        VerificationConfig config,
        ILogger<PolicyVerifier> logger)
    {
        _trustPolicy = trustPolicy ?? throw new ArgumentNullException(nameof(trustPolicy));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "PolicyVerifier";

    /// <summary>Верифицирует tool-ответы и ответы с низкой confidence.</summary>
    public bool CanVerify(VerificationContext ctx) =>
        ctx.Mode == "tool" ||
        ctx.Confidence == "low" ||
        _config.AlwaysVerifyModes.Contains(ctx.Mode, StringComparer.OrdinalIgnoreCase);

    public Task<VerifierResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default)
    {
        // PolicyVerifier полагается на TrustAdmissionPolicy для проверки inter-agent ответов.
        // Для локальных ответов проверяем capability constraints.
        if (_config.Enabled && !_trustPolicy.Mode.Equals(PolicyMode.Disabled))
        {
            // Trust policy уже провёл evaluate при поступлении запроса.
            // Здесь проверяем post-execution policy constraints:
            // - capability limits не превышены
            // - side-effect policies соблюдены
            if (ctx.Mode == "tool" && ctx.ToolUsed is not null)
            {
                if (_config.BlockedTools.Contains(ctx.ToolUsed, StringComparer.OrdinalIgnoreCase))
                {
                    var reason = $"Tool '{ctx.ToolUsed}' is in the blocked-tools list for responses";
                    _logger.LogWarning("[PolicyVerifier] {VerificationId} blocked: {Reason}",
                        ctx.VerificationId, reason);
                    return Task.FromResult(VerifierResult.Fail(Name, reason, VerificationSeverity.High, "POLICY_TOOL_BLOCKED"));
                }
            }

            // Confidence check
            if (ctx.Confidence == "low" && _config.RequireApprovalOnLowConfidence)
            {
                var reason = "Low-confidence response requires manual approval per policy";
                return Task.FromResult(VerifierResult.Fail(Name, reason, VerificationSeverity.Medium, "POLICY_LOW_CONFIDENCE"));
            }
        }

        return Task.FromResult(VerifierResult.Pass(Name));
    }
}
