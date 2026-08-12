namespace Hercules.LLM.JsonRepair;

/// <summary>
///     Service for extracting, repairing, and deserializing JSON from LLM output.
///     Handles common LLM issues: markdown wrappers, trailing commas, extra text around JSON.
/// </summary>
public interface IJsonRepairService
{
    /// <summary>
    ///     Extract and deserialize JSON from LLM text output.
    ///     Tries: (1) direct parse, (2) markdown strip, (3) repair, (4) fallback → null.
    /// </summary>
    /// <typeparam name="T">Target contract type.</typeparam>
    /// <param name="llmText">Raw text output from LLM.</param>
    /// <param name="options">Optional serializer options override.</param>
    /// <returns>Deserialized instance or null if all attempts fail.</returns>
    T? TryParse<T>(string llmText, System.Text.Json.JsonSerializerOptions? options = null) where T : class;

    /// <summary>
    ///     Extract the first valid JSON object from text (no schema validation).
    ///     Handles markdown code blocks, text before/after JSON.
    /// </summary>
    /// <param name="text">Raw LLM output.</param>
    /// <returns>Extracted JSON string or null.</returns>
    string? ExtractJson(string text);

    /// <summary>
    ///     Attempt a "best effort" repair of common JSON malformations
    ///     (trailing commas, single-line comments, unquoted keys in simple cases).
    /// </summary>
    /// <param name="json">Potentially malformed JSON string.</param>
    /// <returns>Repaired JSON string (may still be invalid).</returns>
    string Repair(string json);
}
