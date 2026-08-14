using Hercules.Config;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Verification pipeline: orchestrates multiple verifiers on agent responses.
///     Blocked responses are held for human approval or replaced with safe fallback.
/// </summary>
public interface IVerificationPipeline
{
    /// <summary>Текущая конфигурация pipeline.</summary>
    VerificationConfig Config { get; }

    /// <summary>
    ///     Запустить верификацию ответа.
    ///     Возвращает <see cref="VerificationResult"/> с aggregated outcome.
    /// </summary>
    Task<VerificationResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default);
}
