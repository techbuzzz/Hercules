using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hercules.LLM.JsonRepair;

/// <summary>
///     Default implementation of <see cref="IJsonRepairService" />.
///     Uses a three-pass strategy:
///     1. Direct parse (LLM returned clean JSON)
///     2. Markdown strip → parse (LLM wrapped in ```json ... ```)
///     3. Repair + parse (LLM added trailing commas, extra text, etc.)
/// </summary>
public sealed class JsonRepairService : IJsonRepairService
{
    private static readonly JsonSerializerOptions DefaultOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public T? TryParse<T>(string llmText, JsonSerializerOptions? options = null) where T : class
    {
        options ??= DefaultOpts;

        // Pass 1: direct parse
        if (TryDeserialize<T>(llmText.Trim(), options, out var direct))
        {
            return direct;
        }

        // Pass 2: strip markdown code blocks
        string? extracted = ExtractJson(llmText);
        if (extracted is not null && TryDeserialize<T>(extracted, options, out var fromMarkdown))
        {
            return fromMarkdown;
        }

        // Pass 3: repair malformations → parse
        if (extracted is not null)
        {
            string repaired = Repair(extracted);
            if (TryDeserialize<T>(repaired, options, out var fromRepair))
            {
                return fromRepair;
            }
        }

        // Pass 3b: repair the original text (in case ExtractJson failed)
        string repairedOriginal = Repair(llmText.Trim());
        if (repairedOriginal != llmText.Trim() && TryDeserialize<T>(repairedOriginal, options, out var fromOriginalRepair))
        {
            return fromOriginalRepair;
        }

        return null;
    }

    /// <inheritdoc />
    public string? ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        // Strategy 1: markdown code block ```json ... ``` or ``` ... ```
        var mdMatch = Regex.Match(trimmed,
            @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
            RegexOptions.Singleline);
        if (mdMatch.Success)
        {
            return mdMatch.Groups[1].Value;
        }

        // Strategy 2: raw JSON object — find first { and last }
        var firstBrace = trimmed.IndexOf('{');
        if (firstBrace >= 0)
        {
            var lastBrace = trimmed.LastIndexOf('}');
            if (lastBrace > firstBrace)
            {
                return trimmed[firstBrace..(lastBrace + 1)];
            }
        }

        // Strategy 3: raw JSON array — find first [ and matching ]
        var firstBracket = trimmed.IndexOf('[');
        if (firstBracket >= 0)
        {
            var endBracket = FindMatchingBracket(trimmed, firstBracket);
            if (endBracket >= 0)
            {
                return trimmed[firstBracket..(endBracket + 1)];
            }
        }

        return null;
    }

    /// <inheritdoc />
    public string Repair(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        var result = json;

        // 1. Remove trailing commas before closing braces/brackets
        result = Regex.Replace(result, @",(\s*[}\]])", "$1");

        // 2. Remove single-line // comments (aggressive but safe for JSON-like repair)
        result = Regex.Replace(result, @"//[^\n\r]*", "");

        // 3. Remove multi-line /* ... */ comments
        result = Regex.Replace(result, @"/\*[\s\S]*?\*/", "");

        // 4. Remove leading/trailing text that isn't valid JSON.
        //    Detect whether the JSON is an array or object by looking at the first
        //    significant character (after leading whitespace / text).
        var firstBrace = result.IndexOf('{');
        var firstBracket = result.IndexOf('[');
        var firstSignificant = firstBrace >= 0 && firstBracket >= 0
            ? Math.Min(firstBrace, firstBracket)
            : firstBrace >= 0
                ? firstBrace
                : firstBracket;

        if (firstSignificant < 0)
        {
            return result.Trim();
        }

        var firstChar = result.TrimStart().FirstOrDefault();
        if (firstBracket >= 0 && (firstBrace < 0 || firstBracket < firstBrace))
        {
            // Array starts before object (or no object at all)
            var endIdx = FindMatchingBracket(result, firstBracket);
            if (endIdx >= 0)
            {
                result = result[firstBracket..(endIdx + 1)];
            }
        }
        else
        {
            // Object (possibly with leading text)
            var lastBrace = result.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                result = result[firstBrace..(lastBrace + 1)];
            }
        }

        return result.Trim();
    }

    /// <summary>
    ///     Find the index of the closing ] matching the opening [ at <paramref name="start" />.
    ///     Skips nested brackets and quoted [ ] characters.
    /// </summary>
    private static int FindMatchingBracket(string s, int start)
    {
        if (start < 0 || start >= s.Length || s[start] != '[')
        {
            return -1;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool TryDeserialize<T>(string json, JsonSerializerOptions options, out T? instance) where T : class
    {
        instance = null;
        try
        {
            var trimmed = json.TrimStart();
            // Must be a non-empty object (starts with {)
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                return false;
            }

            // Validate it's actually an object by checking for a matching }
            if (!trimmed.Contains('}'))
            {
                return false;
            }

            var result = JsonSerializer.Deserialize<T>(json, options);
            if (result is null)
            {
                return false;
            }

            instance = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
