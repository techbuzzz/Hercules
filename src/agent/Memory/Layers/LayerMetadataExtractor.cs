using Hercules.LLM;

namespace Hercules.Memory.Layers;

/// <summary>
///     LLM-based extraction of MemoryEntry metadata from raw content.
///     Used by PersistSessionAsync to attach proper metadata to extracted facts.
/// </summary>
public sealed class LayerMetadataExtractor
{
    private readonly ILLMClient _llm;

    private const string JsonSchema = """
        {
          "source": "session_extract" | "user" | "agent" | "skill:{id}",
          "confidence": "High" | "Medium" | "Low",
          "sensitivity": "Public" | "Internal" | "Sensitive" | "Restricted",
          "ttl_minutes": 0 | 60 | 1440 | 10080,
          "tags": ["tag1", "tag2"]
        }
        """;

    public LayerMetadataExtractor(ILLMClient llm)
    {
        _llm = llm;
    }

    /// <summary>
    ///     Analyze raw content and return suggested MemoryEntry metadata.
    ///     Falls back to defaults if LLM call fails.
    /// </summary>
    public async Task<MemoryEntry> ExtractMetadataAsync(
        string content,
        string? context = null,
        CancellationToken ct = default)
    {
        try
        {
            var text = content.Length > 500 ? content[..500] : content;
            var prompt = $"""
                Проанализируй текст и верни СТРОГО JSON-объект с метаданными памяти.
                Не добавляй пояснений, только JSON.

                {JsonSchema}

                Правила:
                - source: кто источник — "session_extract" для LLM-извлечений, "user" для пользовательских фактов
                - confidence: High = подтверждено несколькими источниками, Medium = извлечено из диалога, Low = предположение
                - sensitivity: Restricted = пароли/ключи/секреты, Sensitive = персональные данные, Internal = деловая информация, Public = публичная
                - ttl_minutes: 0 = навсегда, 60 = час, 1440 = день, 10080 = неделя
                - tags: релевантные теги: "user_fact", "project", "preference", "entity", etc.

                Текст:
                {text}
                """;

            var resp = await _llm.CompleteAsync(new[]
            {
                new ChatTurn(ChatRole.System, "Ты — модуль извлечения метаданных памяти. Отвечай ТОЛЬКО JSON."),
                new ChatTurn(ChatRole.User, prompt)
            }, ct);

            return ParseMetadata(resp.Text);
        }
        catch
        {
            // Fallback: safe defaults
            return new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, DateTime.UtcNow, new List<string>());
        }
    }

    private static MemoryEntry ParseMetadata(string json)
    {
        try
        {
            // Try to find JSON object in response
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end < 0) return Default();

            var obj = json[start..(end + 1)];
            using var doc = System.Text.Json.JsonDocument.Parse(obj);
            var root = doc.RootElement;

            var source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "session_extract" : "session_extract";
            var confidence = root.TryGetProperty("confidence", out var c) && Enum.TryParse<MemoryConfidence>(c.GetString(), out var conf)
                ? conf
                : MemoryConfidence.Medium;
            var sensitivity = root.TryGetProperty("sensitivity", out var sen) && Enum.TryParse<MemorySensitivity>(sen.GetString(), out var sens)
                ? sens
                : MemorySensitivity.Internal;
            var ttl = root.TryGetProperty("ttl_minutes", out var t) ? t.GetInt32() : 0;
            var tags = root.TryGetProperty("tags", out var tg)
                ? tg.EnumerateArray().Select(t => t.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                : new List<string>();

            return new MemoryEntry(source, confidence, ttl, sensitivity, DateTime.UtcNow, tags);
        }
        catch
        {
            return Default();
        }

        static MemoryEntry Default() => new("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, DateTime.UtcNow, new List<string>());
    }
}
