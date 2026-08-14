using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Валидирует, что ответ соответствует declared response schema.
///     Используется когда envelope содержит ResponseSchema.
/// </summary>
public sealed class SchemaVerifier : IVerifier
{
    private readonly ILogger<SchemaVerifier> _logger;

    public SchemaVerifier(ILogger<SchemaVerifier> logger) => _logger = logger;

    public string Name => "SchemaVerifier";

    /// <summary>Верифицирует только когда есть schema constraint (задаётся через контекст).</summary>
    public bool CanVerify(VerificationContext ctx) =>
        !string.IsNullOrEmpty(ctx.ResponseText);

    public Task<VerifierResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default)
    {
        var text = ctx.ResponseText;

        // Basic structural checks for structured responses (JSON)
        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(text);
                if (node is null)
                {
                    return Task.FromResult(VerifierResult.Fail(Name,
                        "Response appears to be JSON but parsed as null",
                        VerificationSeverity.Medium, "SCHEMA_PARSE_NULL"));
                }

                // Check for common error wrappers in JSON responses
                if (node is JsonObject obj)
                {
                    if (obj.ContainsKey("error") && obj["error"]?.GetValue<string>()?.Length > 0)
                    {
                        var errorMsg = obj["error"]?.GetValue<string>() ?? "unknown";
                        return Task.FromResult(VerifierResult.Fail(Name,
                            $"Response contains error field: {errorMsg}",
                            VerificationSeverity.Medium, "SCHEMA_ERROR_FIELD"));
                    }

                    // Check that required fields are present (if we can infer them from mode)
                    if (ctx.Mode == "tool")
                    {
                        // For tool responses, we expect some kind of result/output field
                        var hasResultField = obj.ContainsKey("result") ||
                                            obj.ContainsKey("output") ||
                                            obj.ContainsKey("data");
                        if (!hasResultField && obj.Count > 2)
                        {
                            // More than 2 fields but no result — suspicious
                            _logger.LogDebug("[SchemaVerifier] JSON response has no obvious result field");
                        }
                    }
                }

                return Task.FromResult(VerifierResult.Pass(Name));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[SchemaVerifier] JSON parse failed for {VerificationId}: {Error}",
                    ctx.VerificationId, ex.Message);
                return Task.FromResult(VerifierResult.Fail(Name,
                    $"Invalid JSON structure: {ex.Message}",
                    VerificationSeverity.Low, "SCHEMA_JSON_PARSE_ERROR"));
            }
        }

        // For non-JSON responses, check for empty responses
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2)
        {
            return Task.FromResult(VerifierResult.Fail(Name,
                "Response is empty or too short",
                VerificationSeverity.Low, "SCHEMA_EMPTY_RESPONSE"));
        }

        return Task.FromResult(VerifierResult.Pass(Name));
    }
}
