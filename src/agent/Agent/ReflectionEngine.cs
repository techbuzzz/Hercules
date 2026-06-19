using System.Text;
using Hercules.LLM;
using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>Итог рефлексии для вывода пользователю.</summary>
public sealed record ReflectionResult(string Markdown, string FilePath);

/// <summary>
///     Reflection Engine — самоанализ агента после сессии или каждые N команд.
///     Анализирует: что сделано хорошо/плохо, что улучшить, нужно ли обновить навыки.
///     Результат сохраняется в Skills/self_reflection_{timestamp}.md.
/// </summary>
public sealed class ReflectionEngine(
    ILLMClient llm,
    SqliteSessionStore sessions,
    FileSkillRepository skillRepo,
    SkillManager skills)
{
    /// <summary>Выполнить рефлексию по текущей сессии.</summary>
    public async Task<ReflectionResult> ReflectAsync(string sessionId, CancellationToken ct = default)
    {
        List<InteractionLog> lowConf = sessions.GetLowConfidence(sessionId);
        var (skillCount, directCount) = sessions.GetModeStats(sessionId);
        List<Skill> needImprove = skills.SkillsNeedingImprovement();

        var lowConfText = lowConf.Count == 0
            ? "Ответов с низкой уверенностью не было."
            : string.Join("\n", lowConf.Select(l => $"- Запрос: {l.Input} | Ответ: {Trunc(l.Output, 160)}"));

        var prompt = $"""
                       Проведи самоанализ работы ассистента за сессию. Ответь на русском, кратко,
                       строго по структуре с заголовками:

                       ## Что получилось хорошо
                       ## Что получилось плохо
                       ## Что улучшить
                       ## Рекомендации по навыкам

                       Статистика сессии:
                       - Ответов через навыки: {skillCount}
                       - Прямых ответов (direct): {directCount}
                       - Ответы с низкой уверенностью:
                       {lowConfText}
                       - Навыки, требующие улучшения: {(needImprove.Count == 0 ? "нет" : string.Join(", ", needImprove.Select(s => s.Meta.Name)))}
                       """;

        string analysis;
        try
        {
            LlmResponse resp = await llm.CompleteAsync([
                new ChatTurn(ChatRole.System, "Ты — модуль рефлексии самообучающегося агента."),
                new ChatTurn(ChatRole.User, prompt)
            ], ct);
            analysis = resp.Text;
        }
        catch (Exception ex)
        {
            analysis = $"_LLM недоступна для рефлексии: {ex.Message}_";
        }

        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var sb = new StringBuilder();
        sb.AppendLine($"# Рефлексия — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"- Сессия: `{sessionId}`");
        sb.AppendLine($"- Навыки/Direct: {skillCount}/{directCount}");
        sb.AppendLine($"- Низкая уверенность: {lowConf.Count}");
        sb.AppendLine();
        sb.AppendLine(analysis);
        if (needImprove.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Навыки-кандидаты на улучшение");
            foreach (Skill s in needImprove)
            {
                sb.AppendLine($"- `{s.Meta.Id}` {s.Meta.Name} (success_rate={s.Meta.SuccessRate}, uses={s.Meta.TotalUses})");
            }
        }

        var fileName = $"self_reflection_{ts}.md";
        skillRepo.SaveRawMarkdown(fileName, sb.ToString());
        return new ReflectionResult(sb.ToString(), fileName);
    }

    private static string Trunc(string s, int n)
    {
        return s.Length <= n
            ? s
            : s[..n] + "…";
    }
}
