namespace Hercules.Mesh.Verification;

/// <summary>
///     Базовый контракт verifier'а. Каждый verifier проверяет один аспект ответа.
///     Verifiers stateless и могут выполняться параллельно.
/// </summary>
public interface IVerifier
{
    /// <summary>Имя verifier'а (уникальное в рамках pipeline).</summary>
    string Name { get; }

    /// <summary>
    ///     Какие типы ответов верифицировать. Возвращает true если verifier применим.
    /// </summary>
    bool CanVerify(VerificationContext ctx);

    /// <summary>
    ///     Выполнить верификацию контекста. Никогда не бросает —
    ///     все ошибки возвращаются как VerifierResult с Passed=false.
    /// </summary>
    Task<VerifierResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default);
}

/// <summary>
///     Extension methods для IVerifier.
/// </summary>
public static class VerifierExtensions
{
    /// <summary>
    ///     Создать VerifierResult.Pass для этого verifier'а.
    /// </summary>
    public static VerifierResult Pass(this IVerifier verifier) => VerifierResult.Pass(verifier.Name);

    /// <summary>
    ///     Создать VerifierResult.Fail для этого verifier'а.
    /// </summary>
    public static VerifierResult Fail(this IVerifier verifier, string reason,
        VerificationSeverity severity = VerificationSeverity.Medium, string? errorCode = null) =>
        VerifierResult.Fail(verifier.Name, reason, severity, errorCode);
}
