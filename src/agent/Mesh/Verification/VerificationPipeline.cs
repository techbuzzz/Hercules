using System.Diagnostics;
using Hercules.Config;
using HerculesBus.Core;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Orchestrates multiple <see cref="IVerifier"/> instances.
///     Runs applicable verifiers sequentially, aggregates results, blocks if severity ≥ configured threshold.
/// </summary>
public sealed class VerificationPipeline : IVerificationPipeline
{
    private readonly IReadOnlyList<IVerifier> _verifiers;
    private readonly VerificationConfig _config;
    private readonly ILogger<VerificationPipeline> _logger;

    public VerificationPipeline(
        IEnumerable<IVerifier> verifiers,
        VerificationConfig config,
        ILogger<VerificationPipeline> logger)
    {
        _verifiers = verifiers.ToList();
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public VerificationConfig Config => _config;

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var verificationId = string.IsNullOrEmpty(ctx.VerificationId)
            ? Ulid.NewId()
            : ctx.VerificationId;

        // Pipeline disabled
        if (!_config.Enabled)
        {
            _logger.LogDebug("[VerificationPipeline] Pipeline disabled — allowing response {VerificationId}",
                verificationId);
            sw.Stop();
            return VerificationResult.Success(verificationId, Array.Empty<VerifierResult>(), sw.ElapsedMilliseconds);
        }

        var isDryRun = _config.Mode.Equals("dryrun", StringComparison.OrdinalIgnoreCase);
        var ctxWithId = ctx with { VerificationId = verificationId, IsDryRun = isDryRun };

        _logger.LogDebug(
            "[VerificationPipeline] Starting verification {VerificationId} for response (mode={Mode}, tool={Tool}, confidence={Confidence})",
            verificationId, ctx.Mode, ctx.ToolUsed ?? "(none)", ctx.Confidence);

        var results = new List<VerifierResult>();

        foreach (var verifier in _verifiers)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!verifier.CanVerify(ctxWithId))
                continue;

            try
            {
                var result = await verifier.VerifyAsync(ctxWithId, ct);
                results.Add(result);

                if (!result.Passed)
                {
                    _logger.LogWarning(
                        "[VerificationPipeline] {VerifierName} FAILED (severity={Severity}): {Reason}",
                        result.VerifierName, result.Severity, result.Reason);
                }
                else
                {
                    _logger.LogDebug("[VerificationPipeline] {VerifierName} passed", result.VerifierName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VerificationPipeline] {VerifierName} threw — treating as pass",
                    verifier.Name);
                results.Add(new VerifierResult(verifier.Name, Passed: true,
                    "Verifier threw exception (best-effort pass)"));
            }
        }

        sw.Stop();

        // Determine if we should block
        var failures = results.Where(r => !r.Passed).ToList();
        var maxSeverity = failures.Count > 0
            ? failures.Max(f => f.Severity)
            : VerificationSeverity.Low;

        var blockThreshold = ParseSeverity(_config.BlockSeverityThreshold);
        var shouldBlock = failures.Count > 0 && maxSeverity >= blockThreshold && !isDryRun;

        _logger.LogDebug(
            "[VerificationPipeline] Finished {VerificationId}: {Passed} (failures={FailureCount}, maxSeverity={MaxSeverity}, blocked={Blocked}, dryRun={DryRun}, elapsedMs={ElapsedMs})",
            verificationId,
            !shouldBlock,
            failures.Count,
            maxSeverity,
            shouldBlock,
            isDryRun,
            sw.ElapsedMilliseconds);

        if (shouldBlock)
        {
            var reason = string.Join("; ", failures.Select(f => $"{f.VerifierName}: {f.Reason}"));
            return VerificationResult.BlockedResult(verificationId, results, maxSeverity, reason, sw.ElapsedMilliseconds);
        }

        return VerificationResult.Success(verificationId, results, sw.ElapsedMilliseconds, isDryRun);
    }

    /// <summary>
    ///     Parse a severity string to enum. Returns Low as safe default.
    /// </summary>
    private static VerificationSeverity ParseSeverity(string? level) =>
        level?.ToLowerInvariant() switch
        {
            "info" => VerificationSeverity.Info,
            "low" => VerificationSeverity.Low,
            "medium" => VerificationSeverity.Medium,
            "high" => VerificationSeverity.High,
            "critical" => VerificationSeverity.Critical,
            _ => VerificationSeverity.Low
        };
}
