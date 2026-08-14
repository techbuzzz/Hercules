namespace Hercules.Mesh.Verification;

/// <summary>
///     Результат одного verifier'а.
/// </summary>
public sealed record VerifierResult(
    string VerifierName,
    bool Passed,
    string? Reason = null,
    string? ErrorCode = null,
    VerificationSeverity Severity = VerificationSeverity.Low,
    IReadOnlyDictionary<string, string>? Details = null)
{
    public static VerifierResult Pass(string name) =>
        new(name, Passed: true, Severity: VerificationSeverity.Low);

    public static VerifierResult Fail(string name, string reason, VerificationSeverity severity,
        string? errorCode = null, IReadOnlyDictionary<string, string>? details = null) =>
        new(name, Passed: false, reason, errorCode, severity, details);
}

/// <summary>
///     Severity уровни для результатов верификации.
/// </summary>
public enum VerificationSeverity
{
    /// <summary>Информационное замечание, не блокирует.</summary>
    Info,

    /// <summary>Низкий риск — пропускается по умолчанию.</summary>
    Low,

    /// <summary>Средний риск — блокирует в enforce-режиме.</summary>
    Medium,

    /// <summary>Высокий риск — блокирует всегда.</summary>
    High,

    /// <summary>Критический риск — блокирует, логируется как инцидент.</summary>
    Critical
}

/// <summary>
///     Результат всего verification pipeline.
/// </summary>
public sealed class VerificationResult
{
    /// <summary>ID верификации.</summary>
    public string VerificationId { get; init; } = "";

    /// <summary>true если pipeline успешно пройден (все verifiers passed или severity &lt; block threshold).</summary>
    public bool Passed { get; init; }

    /// <summary>true если хотя бы один verifier заблокировал ответ.</summary>
    public bool Blocked => !Passed;

    /// <summary>Результаты отдельных verifiers в порядке выполнения.</summary>
    public List<VerifierResult> VerifierResults { get; init; } = new();

    /// <summary>Общий reason для блокировки (concatenated).</summary>
    public string? BlockingReason { get; init; }

    /// <summary>Максимальная severity среди провалившихся verifiers.</summary>
    public VerificationSeverity MaxSeverity { get; init; } = VerificationSeverity.Low;

    /// <summary>Время верификации в миллисекундах.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>True если это был dry-run (результат залогирован, но не заблокирован).</summary>
    public bool WasDryRun { get; init; }

    /// <summary>Создать успешный результат.</summary>
    public static VerificationResult Success(
        string verificationId,
        IReadOnlyList<VerifierResult> results,
        long elapsedMs,
        bool wasDryRun = false) =>
        new()
        {
            VerificationId = verificationId,
            Passed = true,
            VerifierResults = results.ToList(),
            ElapsedMs = elapsedMs,
            WasDryRun = wasDryRun
        };

    /// <summary>Создать заблокированный результат.</summary>
    public static VerificationResult BlockedResult(
        string verificationId,
        IReadOnlyList<VerifierResult> results,
        VerificationSeverity maxSeverity,
        string blockingReason,
        long elapsedMs,
        bool wasDryRun = false) =>
        new()
        {
            VerificationId = verificationId,
            Passed = false,
            VerifierResults = results.ToList(),
            MaxSeverity = maxSeverity,
            BlockingReason = blockingReason,
            ElapsedMs = elapsedMs,
            WasDryRun = wasDryRun
        };
}
