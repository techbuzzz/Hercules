namespace Hercules.Config;

/// <summary>
///     Сервис маскирования секретов и разрешения secret references (task_015).
///     - MaskSecrets: redact PII, API keys, bearer tokens from arbitrary text.
///     - ResolveSecret: resolve "env:VAR_NAME" references to actual values.
///     - IsSecretReference: check if a string looks like a secret reference.
/// </summary>
public interface ISecretMaskingService
{
    /// <summary>
    ///     Redact all known secrets (API keys, bearer tokens, emails, phones, credit cards)
    ///     from the given text. Uses IRedactionService internally.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <returns>Text with secrets replaced by markers.</returns>
    string MaskSecrets(string text);

    /// <summary>
    ///     Resolve a secret reference (e.g. "env:API_KEY") to its actual value.
    ///     Returns null if the referenced secret is not found.
    /// </summary>
    /// <param name="reference">Reference string, e.g. "env:VAR_NAME".</param>
    /// <returns>Resolved value or null.</returns>
    string? ResolveSecret(string reference);

    /// <summary>
    ///     Check whether the given text looks like a secret reference
    ///     (starts with the configured SecretReferencePrefix).
    /// </summary>
    /// <param name="text">Text to check.</param>
    /// <returns>True if this is a secret reference.</returns>
    bool IsSecretReference(string text);

    /// <summary>
    ///     Expand all secret references in a string to their resolved values.
    ///     If a reference cannot be resolved, it is left unchanged.
    /// </summary>
    /// <param name="text">Text containing secret references.</param>
    /// <returns>Text with expanded secret values.</returns>
    string ExpandSecretReferences(string text);
}
