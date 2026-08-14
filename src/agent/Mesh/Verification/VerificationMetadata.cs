namespace Hercules.Mesh.Verification;

/// <summary>
///     Метаданные верификации, прикрепляемые к ответу агента.
///     Показывает, прошёл ли ответ верификацию, severity и причину блокировки.
/// </summary>
public sealed record VerificationMetadata(
    string VerificationId,
    bool Passed,
    string Severity,
    string? BlockingReason,
    long ElapsedMs);
