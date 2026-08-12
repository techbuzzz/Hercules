using System.Text.RegularExpressions;
using Hercules.Redaction;

namespace Hercules.Config;

/// <summary>
///     Implementation of ISecretMaskingService (task_015).
///     Uses IRedactionService for pattern-based redaction and reads environment variables
///     for secret reference resolution.
/// </summary>
public sealed partial class SecretMaskingService : ISecretMaskingService
{
    private readonly SecretsConfig _config;
    private readonly IRedactionService _redaction;

    public SecretMaskingService(SecretsConfig config, IRedactionService redaction)
    {
        _config = config;
        _redaction = redaction;
    }

    public string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        // 1. Redact known PII/secret patterns via redaction service
        var result = _redaction?.Redact(text, sensitivity: "high") ?? text;

        // 2. Also redact any literal env var values that appear in the text
        //    (in case the actual secret value leaked into the text)
        foreach (var (name, value) in LoadSecrets())
        {
            if (!string.IsNullOrEmpty(value) && result.Contains(value, StringComparison.Ordinal))
            {
                result = result.Replace(value, $"${{{name}}}");
            }
        }

        return result;
    }

    public string? ResolveSecret(string reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        if (!IsSecretReference(reference))
        {
            return null;
        }

        var shortName = reference[_config.SecretReferencePrefix.Length..];

        // Prepend the configured prefix to look up the full env var name
        // e.g. "env:MYAPIKEY" + prefix "HERCULES_SECRET_" → "HERCULES_SECRET_MYAPIKEY"
        var fullName = _config.EnvironmentVariablePrefix + shortName;

        // Case-insensitive lookup with the full prefixed name
        var envVars = Environment.GetEnvironmentVariables();
        foreach (string key in envVars.Keys)
        {
            if (key.Equals(fullName, StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetEnvironmentVariable(key);
            }
        }

        // Fallback: try exact full name
        return Environment.GetEnvironmentVariable(fullName);
    }

    public bool IsSecretReference(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.StartsWith(_config.SecretReferencePrefix, StringComparison.Ordinal);
    }

    public string ExpandSecretReferences(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Replace all "env:VAR_NAME" references with their actual values
        var result = text;
        foreach (Match match in EnvReferenceRegex().Matches(text))
        {
            var reference = match.Value;
            var resolved = ResolveSecret(reference);
            if (resolved is not null)
            {
                result = result.Replace(reference, resolved);
            }
        }

        return result;
    }

    /// <summary>
    ///     Load all secrets from environment variables matching the configured prefix.
    ///     Returns a dictionary of (name, value) pairs.
    /// </summary>
    private Dictionary<string, string> LoadSecrets()
    {
        var prefix = _config.EnvironmentVariablePrefix;
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(prefix))
        {
            return secrets;
        }

        var envVars = Environment.GetEnvironmentVariables();
        foreach (string key in envVars.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(value))
                {
                    // Strip the prefix for the short name
                    var shortName = key[prefix.Length..];
                    secrets[shortName] = value;
                    secrets[key] = value;
                }
            }
        }

        return secrets;
    }

    [GeneratedRegex(@"env:[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled)]
    private static partial Regex EnvReferenceRegex();
}
