using System.Text;
using Hercules.LLM;
using Hercules.Memory.Layers;
using Hercules.Storage;
using Microsoft.Extensions.ObjectPool;

namespace Hercules.Agent;

/// <summary>
///     Управляет долговременной памятью: загрузка контекста при старте сессии,
///     извлечение фактов о пользователе после сессии, сохранение в Markdown.
///     Uses layered memory (request context, working memory, durable facts, episodic).
/// </summary>
public sealed class MemoryManager(
    MemoryStore store,
    ILLMClient llm,
    LayeredMemoryManager? layered = null)
{
    // task_084: pooled StringBuilder for context assembly and section parsing.
    private static readonly ObjectPool<StringBuilder> SbPool =
        new DefaultObjectPoolProvider().CreateStringBuilderPool();

    public string ProfileMarkdown => store.ReadProfile();

    public string PreferencesMarkdown => store.ReadPreferences();

    public string EntitiesMarkdown => store.ReadEntities();

    /// <summary>Собрать контекст для системного промпта (профиль + предпочтения + последний контекст).</summary>
    public string BuildContextBlock() => BuildContextBlock(sessionId: null);

    /// <summary>
    ///     Собрать контекст для конкретной сессии (task_075 — H6 fix).
    ///     Когда <paramref name="sessionId" /> задан и <see cref="LayeredMemoryManager" />
    ///     поддерживает per-session context, рабочая память берётся только для этой сессии —
    ///     иначе параллельные HTTP-запросы делили бы один <c>IWorkingMemory</c>.
    /// </summary>
    public string BuildContextBlock(string? sessionId)
    {
        // Use layered manager for new layered context (null-safe, session-aware when available)
        string layeredBlock = layered is not null
            ? (sessionId is not null
                ? layered.BuildContextBlockAsync(sessionId).GetAwaiter().GetResult()
                : layered.BuildContextBlockAsync().GetAwaiter().GetResult())
            : "";

        // Fall back to legacy profile/prefs/entities for backward compatibility
        // task_084: pooled StringBuilder.
        var sb = SbPool.Get();
        try
        {
            sb.AppendLine("=== ПАМЯТЬ О ПОЛЬЗОВАТЕЛЕ ===");
            sb.AppendLine(store.ReadProfile().Trim());
            sb.AppendLine();
            sb.AppendLine(store.ReadPreferences().Trim());
            sb.AppendLine();
            sb.AppendLine(store.ReadEntities().Trim());
            var lastCtx = store.ReadLastContext();
            if (!string.IsNullOrWhiteSpace(lastCtx))
            {
                sb.AppendLine();
                sb.AppendLine("=== КОНТЕКСТ ПРОШЛЫХ СЕССИЙ ===");
                sb.AppendLine(lastCtx.Trim());
            }

            // Append layered block if non-empty
            if (!string.IsNullOrWhiteSpace(layeredBlock))
            {
                sb.AppendLine();
                sb.Append(layeredBlock);
            }

            return sb.ToString();
        }
        finally
        {
            SbPool.Return(sb);
        }
    }

    /// <summary>Перезаписать профиль пользователя (Web API: PUT /api/memory/profile).</summary>
    public void UpdateProfile(string markdown)
    {
        store.WriteProfile(markdown);
    }

    public void Reset()
    {
        store.Reset();
        layered?.ClearWorking();
    }

    /// <summary>
    ///     По завершении сессии: попросить LLM извлечь из диалога краткое содержание,
    ///     новые факты о пользователе, сущности и предпочтения; сохранить в память.
    ///     Now uses LayerMetadataExtractor for proper metadata on durable facts.
    /// </summary>
    public async Task PersistSessionAsync(IReadOnlyList<ChatTurn> transcript, string sessionId, CancellationToken ct = default)
    {
        if (transcript.Count == 0)
        {
            return;
        }

        var dialog = string.Join("\n", transcript
            .Where(t => t.Role != ChatRole.System)
            .Select(t => $"{(t.Role == ChatRole.User ? "Пользователь" : "Ассистент")}: {t.Content}"));

        var prompt = $$"""
                       Проанализируй диалог и верни СТРОГО в формате Markdown четыре секции.
                       Если данных для секции нет — оставь "(нет нового)".

                       ### SUMMARY
                       (3-5 предложений краткого содержания сессии)

                       ### PROFILE
                       (новые факты о пользователе: имя, стиль общения, язык, привычки — маркированный список или "(нет нового)")

                       ### ENTITIES
                       (упомянутые проекты/люди/компании — маркированный список или "(нет нового)")

                       ### PREFERENCES
                       (предпочтения по формату/тону ответов — маркированный список или "(нет нового)")

                       Диалог:
                       {{dialog}}
                       """;

        LlmResponse resp = await llm.CompleteAsync(new[]
        {
            new ChatTurn(ChatRole.System, "Ты — модуль памяти. Извлекаешь факты строго по формату."),
            new ChatTurn(ChatRole.User, prompt)
        }, ct);

        Dictionary<string, string> sections = ParseSections(resp.Text);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var extractedAt = DateTime.UtcNow;

        if (sections.TryGetValue("SUMMARY", out var summary) && IsMeaningful(summary))
        {
            // Append to legacy episodic context
            store.AppendContext(summary, today);

            // Also append as episodic record with metadata
            var episodeEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, extractedAt, new List<string> { "summary" });
            if (layered is not null) await layered.AppendEpisodeAsync(sessionId, summary, episodeEntry, ct);
        }

        if (sections.TryGetValue("PROFILE", out var profile) && IsMeaningful(profile))
        {
            store.Append(store.ProfilePath, $"\n## Обновление {today:yyyy-MM-dd}\n{profile}");

            // Store as durable fact with metadata
            var factEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, extractedAt, new List<string> { "user_fact", "profile" });
            if (layered is not null) await layered.StoreFactAsync("profile_update", profile, factEntry, ct);
        }

        if (sections.TryGetValue("ENTITIES", out var entities) && IsMeaningful(entities))
        {
            store.Append(store.EntitiesPath, $"\n## Обновление {today:yyyy-MM-dd}\n{entities}");

            var entityEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, extractedAt, new List<string> { "entity" });
            if (layered is not null) await layered.StoreFactAsync("entities_update", entities, entityEntry, ct);
        }

        if (sections.TryGetValue("PREFERENCES", out var prefs) && IsMeaningful(prefs))
        {
            store.Append(store.PreferencesPath, $"\n## Обновление {today:yyyy-MM-dd}\n{prefs}");

            var prefsEntry = new MemoryEntry("session_extract", MemoryConfidence.Medium, 0, MemorySensitivity.Internal, extractedAt, new List<string> { "preference" });
            if (layered is not null) await layered.StoreFactAsync("preferences_update", prefs, prefsEntry, ct);
        }

        // Cleanup expired facts
        if (layered is not null) await layered.CleanupExpiredAsync(ct);
    }

    private static bool IsMeaningful(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && !text.Contains("(нет нового)", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        // task_084: pooled StringBuilder for section parsing.
        var sb = SbPool.Get();
        try
        {
            void Flush()
            {
                if (current is not null)
                {
                    result[current] = sb.ToString().Trim();
                }

                sb.Clear();
            }

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("###"))
                {
                    Flush();
                    current = trimmed.TrimStart('#', ' ').Trim();
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            Flush();
            return result;
        }
        finally
        {
            SbPool.Return(sb);
        }
    }
}
