namespace Hercules.Redaction;

/// <summary>
///     Сервис редизации PII и секретов из логов и telemetry (task_014).
/// </summary>
public interface IRedactionService
{
    /// <summary>
    ///     Redact sensitive data from text based on sensitivity level.
    ///     Uses built-in patterns (API key, email, phone, credit card) and custom config patterns.
    /// </summary>
    /// <param name="text">Source text to redact.</param>
    /// <param name="sensitivity">Sensitivity level: high, medium, low.</param>
    /// <returns>Text with sensitive data replaced by redaction marker.</returns>
    string Redact(string text, string sensitivity = "medium");

    /// <summary>Redact using all patterns regardless of sensitivity.</summary>
    string RedactAll(string text);
}
