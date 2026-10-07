using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hercules.LLM;
using Hercules.Mesh.Router;
using Hercules.Mesh.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Hercules.Mesh.Aggregation;

/// <summary>
///     Aggregates peer responses from fan-out: validates against schema, applies selection
///     strategy (deterministic / voting / LLM-judge), and returns the best response.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_045.
/// </summary>
public sealed class ResponseAggregator
{
    // task_084: pooled StringBuilder for LLM-judge prompt assembly.
    private static readonly ObjectPool<StringBuilder> SbPool =
        new DefaultObjectPoolProvider().CreateStringBuilderPool();

    private readonly FanOutOptions _options;
    private readonly ILLMClient? _llm;
    private readonly ILogger<ResponseAggregator> _logger;

    public ResponseAggregator(
        FanOutOptions options,
        ILLMClient? llm,
        ILogger<ResponseAggregator> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _llm = llm;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Aggregate responses using the configured strategy.
    /// </summary>
    /// <param name="envelope">Original intent envelope (used for LLM-judge context).</param>
    /// <param name="responses">All responses received from peers.</param>
    /// <param name="validResponses">Responses that passed schema validation.</param>
    /// <param name="schemaViolations">Map of agent id → violation reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (winner, method, rationale).</returns>
    public async Task<(IntentResponse? Winner, string Method, string? Rationale)> AggregateAsync(
        IntentEnvelope envelope,
        IReadOnlyList<IntentResponse> responses,
        IReadOnlyList<IntentResponse> validResponses,
        IReadOnlyDictionary<string, string> schemaViolations,
        CancellationToken ct = default)
    {
        if (validResponses.Count == 0)
        {
            _logger.LogDebug("[ResponseAggregator] No valid responses — returning null winner");
            return (null, "no-valid-responses", null);
        }

        if (validResponses.Count == 1)
        {
            return (validResponses[0], "only-valid-response", null);
        }

        return _options.Strategy switch
        {
            FanOutSelectionStrategy.Deterministic =>
                AggregateDeterministic(validResponses),

            FanOutSelectionStrategy.Voting =>
                AggregateVoting(validResponses, responses),

            FanOutSelectionStrategy.LlmJudge =>
                await AggregateWithLlmJudgeAsync(envelope, validResponses, ct),

            _ => AggregateDeterministic(validResponses)
        };
    }

    /// <summary>
    ///     Validate a single response against the requested schema.
    ///     Returns null if valid, or an error reason string if invalid.
    /// </summary>
    public string? ValidateSchema(IntentResponse response, ResponseSchema? schema)
    {
        if (schema == null || !_options.EnableSchemaValidation)
        {
            return null;
        }

        if (response.Result == null)
        {
            return "Response result is null";
        }

        // Type validation
        switch (schema.SchemaType.ToLowerInvariant())
        {
            case "json":
                if (!IsValidJson(response.Result))
                {
                    return $"Response is not valid JSON (expected schema_type=json)";
                }

                // If a JSON schema is provided, do basic structural validation
                if (!string.IsNullOrWhiteSpace(schema.JsonSchema))
                {
                    return ValidateJsonSchema(response.Result, schema.JsonSchema);
                }
                return null;

            case "text":
                // Any non-null string is acceptable
                return null;

            default:
                // Unknown schema type — skip validation
                return null;
        }
    }

    /// <summary>
    ///     Select best response deterministically based on configured criterion.
    /// </summary>
    private (IntentResponse? Winner, string Method, string? Rationale) AggregateDeterministic(
        IReadOnlyList<IntentResponse> responses)
    {
        var successful = responses.Where(r => r.IsSuccess).ToList();
        if (successful.Count == 0)
        {
            return (null, "deterministic-no-success", null);
        }

        IntentResponse winner = _options.DeterministicCriterion switch
        {
            DeterministicCriterion.FirstSuccess =>
                // Return in original order (already sorted by arrival time in fan-out)
                successful[0],

            DeterministicCriterion.HighestConfidence =>
                successful.OrderByDescending(r => r.Confidence ?? 0).First(),

            DeterministicCriterion.Latest =>
                successful.OrderByDescending(r => r.TimestampOrUtc).First(),

            _ => successful[0]
        };

        string method = $"deterministic-{_options.DeterministicCriterion.ToString().ToLowerInvariant()}";
        return (winner, method, null);
    }

    /// <summary>
    ///     Select best response by voting. Each peer's response is treated as a vote.
    ///     If no clear majority (threshold not met), falls back to deterministic selection.
    /// </summary>
    private (IntentResponse? Winner, string Method, string? Rationale) AggregateVoting(
        IReadOnlyList<IntentResponse> validResponses,
        IReadOnlyList<IntentResponse> allResponses)
    {
        if (validResponses.Count < _options.MinResponsesForVoting)
        {
            _logger.LogDebug(
                "[ResponseAggregator] Voting: only {Count} responses (min={Min}) — falling back to deterministic",
                validResponses.Count, _options.MinResponsesForVoting);
            return AggregateDeterministic(validResponses);
        }

        // Group by normalized result text
        var groups = validResponses
            .Where(r => r.IsSuccess && r.Result != null)
            .GroupBy(r => NormalizeForVoting(r.Result!))
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count == 0)
        {
            return (null, "voting-no-comparable", null);
        }

        int topCount = groups[0].Count();
        double agreementRatio = (double)topCount / validResponses.Count;

        if (agreementRatio >= _options.VotingThreshold)
        {
            var winner = groups[0].OrderByDescending(r => r.Confidence ?? 0).First();
            return (winner, $"voting-agreement-{agreementRatio:P0}", null);
        }

        // No clear majority — fall back to deterministic
        _logger.LogDebug(
            "[ResponseAggregator] Voting: top agreement {Ratio:P0} < threshold {Threshold:P0} — falling back to deterministic",
            agreementRatio, _options.VotingThreshold);
        return AggregateDeterministic(validResponses);
    }

    /// <summary>
    ///     Use an LLM judge to select the best response from the valid responses.
    ///     Falls back to deterministic selection on LLM failure.
    /// </summary>
    private async Task<(IntentResponse? Winner, string Method, string? Rationale)> AggregateWithLlmJudgeAsync(
        IntentEnvelope envelope,
        IReadOnlyList<IntentResponse> responses,
        CancellationToken ct)
    {
        if (_llm == null)
        {
            _logger.LogWarning("[ResponseAggregator] LLM-judge requested but ILLMClient not available — falling back to deterministic");
            var (fallbackWinner, fallbackMethod, _) = AggregateDeterministic(responses);
            return (fallbackWinner, $"{fallbackMethod}-llm-fallback", "LLM-judge unavailable, used deterministic fallback");
        }

        try
        {
            // task_084: pooled StringBuilder.
            var sb = SbPool.Get();
            string prompt;
            try
            {
                for (var i = 0; i < responses.Count; i++)
                {
                    var r = responses[i];
                    sb.Append($"### [{i + 1}] от {r.Agent} (confidence={r.Confidence?.ToString("P0") ?? "n/a"}, mode={r.Mode})\n");
                    sb.Append(r.Result ?? "(пустой ответ)");
                    sb.Append("\n\n");
                }

                prompt = string.Join("\n",
                    "Ты — судья (LLM-judge) в multi-agent системе. Пользователь задал вопрос,",
                    "и несколько агентов дали ответы. Выбери лучший ответ.",
                    "",
                    $"Вопрос: {envelope.Payload}",
                    "",
                    sb.ToString(),
                    "",
                    "Верни СТРОГО валидный JSON:",
                    "{",
                    "  \"best_index\": <номер лучшего варианта, начиная с 1>,",
                    "  \"rationale\": \"<краткое объяснение выбора на русском, 1-2 предложения>\"",
                    "}");
            }
            finally
            {
                SbPool.Return(sb);
            }

            LlmResponse llmResp = await _llm.CompleteAsync([
                new ChatTurn(ChatRole.System, "Ты — LLM-judge. Возвращаешь только валидный JSON."),
                new ChatTurn(ChatRole.User, prompt)
            ], ct);

            var json = ExtractJson(llmResp.Text);
            using var doc = JsonDocument.Parse(json);
            var bestIndex = doc.RootElement.GetProperty("best_index").GetInt32() - 1;
            var rationale = doc.RootElement.GetProperty("rationale").GetString();

            if (bestIndex >= 0 && bestIndex < responses.Count)
            {
                return (responses[bestIndex], "llm-judge", rationale);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResponseAggregator] LLM-judge failed — falling back to deterministic");
        }

        // Fallback to deterministic on any error
        var (winner, method, _) = AggregateDeterministic(responses);
        return (winner, $"{method}-llm-fallback", "LLM-judge unavailable or failed, used deterministic fallback");
    }

    /// <summary>
    ///     Normalize response text for voting comparison: trim whitespace, collapse spaces.
    /// </summary>
    private static string NormalizeForVoting(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    /// <summary>
    ///     Basic JSON validity check without full schema validation.
    /// </summary>
    private static bool IsValidJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Basic JSON schema structural validation: checks required top-level properties.
    ///     For full Draft-07 validation, a NuGet library would be needed.
    ///     This is a pragmatic lightweight check.
    /// </summary>
    private static string? ValidateJsonSchema(string json, string schemaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var schemaDoc = JsonDocument.Parse(schemaJson);

            var root = doc.RootElement;
            var schema = schemaDoc.RootElement;

            // Check type constraint
            if (schema.TryGetProperty("type", out var typeElement))
            {
                var expectedType = typeElement.GetString();
                var actualType = root.ValueKind switch
                {
                    JsonValueKind.Object => "object",
                    JsonValueKind.Array => "array",
                    JsonValueKind.String => "string",
                    JsonValueKind.Number => "number",
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Null => "null",
                    _ => null
                };

                if (expectedType != null && actualType != expectedType)
                {
                    return $"Type mismatch: expected '{expectedType}', got '{actualType}'";
                }
            }

            // Check required properties
            if (schema.TryGetProperty("required", out var requiredElement) &&
                requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in requiredElement.EnumerateArray())
                {
                    var propName = prop.GetString();
                    if (!string.IsNullOrEmpty(propName) && !root.TryGetProperty(propName, out _))
                    {
                        return $"Missing required property '{propName}'";
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Schema validation error: {ex.Message}";
        }
    }

    /// <summary>
    ///     Extract the first JSON object from an LLM response text.
    /// </summary>
    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : "{}";
    }
}
