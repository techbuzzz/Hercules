using Hercules.Storage;

namespace Hercules.Agent;

/// <summary>Результат маршрутизации запроса.</summary>
/// <param name="MatchedSkill">Найденный навык или null (тогда direct-режим).</param>
/// <param name="Score">Оценка совпадения (количество совпавших триггеров).</param>
public readonly record struct RouteResult(Skill? MatchedSkill, int Score)
{
    public bool IsSkill => MatchedSkill is not null;
}

/// <summary>
///     Маршрутизатор запросов: ищет подходящий навык по триггерам.
///     Если совпадений нет — возвращается direct-режим (обычный ответ LLM).
/// </summary>
public sealed class SkillRouter(SkillManager skills)
{
    /// <summary>
    ///     Подобрать навык по входному тексту. Сопоставление — по вхождению
    ///     триггеров навыка в нормализованный текст запроса. Побеждает навык
    ///     с наибольшим числом совпадений (при равенстве — с большим success_rate).
    /// </summary>
    public RouteResult Route(string input)
    {
        var normalized = Normalize(input);
        Skill? best = null;
        var bestScore = 0;

        foreach (Skill skill in skills.All())
        {
            var score = skill.Meta.Triggers.Count(trigger =>
                !string.IsNullOrWhiteSpace(trigger) &&
                normalized.Contains(Normalize(trigger), StringComparison.Ordinal));

            if (score <= bestScore &&
                (score != bestScore ||
                 score <= 0 ||
                 best is null ||
                 !(skill.Meta.SuccessRate > best.Meta.SuccessRate)))
            {
                continue;
            }

            best = skill;
            bestScore = score;
        }

        return bestScore > 0
            ? new RouteResult(best, bestScore)
            : new RouteResult(null, 0);
    }

    /// <summary>Нормализация запроса для подсчёта повторов (нижний регистр, схлопывание пробелов).</summary>
    public static string Normalize(string text)
    {
        return string.Join(' ', text.ToLowerInvariant()
            .Trim()
            .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'],
                StringSplitOptions.RemoveEmptyEntries));
    }
}
