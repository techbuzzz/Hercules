using System.Text.RegularExpressions;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Redaction;

/// <summary>
///     Конфигурируемый redaction-сервис (task_014).
///     Built-in patterns: API keys, emails, phone numbers, credit cards.
///     Custom patterns from AuditConfig.RedactionPatterns.
/// </summary>
public sealed partial class RedactionService : IRedactionService
{
    private readonly AuditConfig _config;
    private readonly ILogger<RedactionService> _logger;

    // Built-in patterns — compiled once at startup
    private static readonly Regex ApiKeyPattern = BuildApiKeyRegex();
    private static readonly Regex EmailPattern = BuildEmailRegex();
    private static readonly Regex PhonePattern = BuildPhoneRegex();
    private static readonly Regex CreditCardPattern = BuildCreditCardRegex();
    private static readonly Regex BearerTokenPattern = BuildBearerTokenRegex();

    public RedactionService(AuditConfig config, ILogger<RedactionService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string Redact(string text, string sensitivity = "medium")
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // High sensitivity: redact all patterns
        // Medium sensitivity: redact API keys, emails, phones, credit cards
        // Low sensitivity: redact only API keys
        return sensitivity.ToLowerInvariant() switch
        {
            "high" => RedactAll(text),
            "medium" => MediumSensitivityRedact(text),
            "low" => LowSensitivityRedact(text),
            _ => MediumSensitivityRedact(text)
        };
    }

    public string RedactAll(string text)
    {
        var result = text;
        result = BearerTokenPattern.Replace(result, "Bearer ***");
        result = ApiKeyPattern.Replace(result, "[API_KEY]");
        result = EmailPattern.Replace(result, "[EMAIL]");
        result = PhonePattern.Replace(result, "[PHONE]");
        result = CreditCardPattern.Replace(result, "[CARD]");
        result = ApplyCustomPatterns(result);
        return result;
    }

    private string MediumSensitivityRedact(string text)
    {
        var result = text;
        result = BearerTokenPattern.Replace(result, "Bearer ***");
        result = ApiKeyPattern.Replace(result, "[API_KEY]");
        result = EmailPattern.Replace(result, "[EMAIL]");
        result = PhonePattern.Replace(result, "[PHONE]");
        result = CreditCardPattern.Replace(result, "[CARD]");
        result = ApplyCustomPatterns(result);
        return result;
    }

    private string LowSensitivityRedact(string text)
    {
        var result = text;
        result = BearerTokenPattern.Replace(result, "Bearer ***");
        result = ApiKeyPattern.Replace(result, "[API_KEY]");
        result = ApplyCustomPatterns(result);
        return result;
    }

    private string ApplyCustomPatterns(string text)
    {
        if (_config.RedactionPatterns.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var pattern in _config.RedactionPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern.Pattern))
            {
                continue;
            }

            try
            {
                var replacement = string.IsNullOrEmpty(pattern.Replacement) ? "[REDACTED]" : pattern.Replacement;
                result = Regex.Replace(result, pattern.Pattern, replacement, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
            catch (RegexParseException ex)
            {
                _logger.LogWarning("[Redaction] Invalid regex pattern '{Pattern}': {Error}", pattern.Pattern, ex.Message);
            }
        }

        return result;
    }

    // ---- Regex factory methods (partial class for source generators) ----

    [GeneratedRegex(@"\b(sk|pk|token|secret|key|auth)[_-][a-zA-Z0-9]{10,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BuildApiKeyRegex();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex BuildEmailRegex();

    [GeneratedRegex(@"(?:\+\d[\s\-\(\)]{9,18}\d|\(\d{3}\)\s?\d{3}[\s\-]\d{4}|\d{3}[\s\-]\d{3}[\s\-]\d{4}|\+\d[\d\s\-\(\)]{8,13}\d)", RegexOptions.Compiled)]
    private static partial Regex BuildPhoneRegex();

    [GeneratedRegex(@"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex BuildCreditCardRegex();

    [GeneratedRegex(@"Bearer\s+[a-zA-Z0-9_\-\.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BuildBearerTokenRegex();
}
