using System.Text;
using Hercules.LLM;
using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>
///     Управляет долговременной памятью: загрузка контекста при старте сессии,
///     извлечение фактов о пользователе после сессии, сохранение в Markdown.
/// </summary>
public sealed class MemoryManager(MemoryStore store, ILLMClient llm)
{
    public string ProfileMarkdown => store.ReadProfile();

    public string PreferencesMarkdown => store.ReadPreferences();

    public string EntitiesMarkdown => store.ReadEntities();

    /// <summary>Собрать контекст для системного промпта (профиль + предпочтения + последний контекст).</summary>
    public string BuildContextBlock()
    {
        var sb = new StringBuilder();
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

        return sb.ToString();
    }

    /// <summary>Перезаписать профиль пользователя (Web API: PUT /api/memory/profile).</summary>
    public void UpdateProfile(string markdown)
    {
        store.WriteProfile(markdown);
    }

    public void Reset()
    {
        store.Reset();
    }

    /// <summary>
    ///     По завершении сессии: попросить LLM извлечь из диалога краткое содержание,
    ///     новые факты о пользователе, сущности и предпочтения; сохранить в память.
    /// </summary>
    public async Task PersistSessionAsync(IReadOnlyList<ChatTurn> transcript, CancellationToken ct = default)
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

        if (sections.TryGetValue("SUMMARY", out var summary) && IsMeaningful(summary))
        {
            store.AppendContext(summary, today);
        }

        if (sections.TryGetValue("PROFILE", out var profile) && IsMeaningful(profile))
        {
            store.Append(store.ProfilePath, $"\n## Обновление {today:yyyy-MM-dd}\n{profile}");
        }

        if (sections.TryGetValue("ENTITIES", out var entities) && IsMeaningful(entities))
        {
            store.Append(store.EntitiesPath, $"\n## Обновление {today:yyyy-MM-dd}\n{entities}");
        }

        if (sections.TryGetValue("PREFERENCES", out var prefs) && IsMeaningful(prefs))
        {
            store.Append(store.PreferencesPath, $"\n## Обновление {today:yyyy-MM-dd}\n{prefs}");
        }
    }

    private static bool IsMeaningful(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && !text.Contains("(нет нового)", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var sb = new StringBuilder();

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
}
