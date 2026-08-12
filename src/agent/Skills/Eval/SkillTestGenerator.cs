using System.Security.Cryptography;
using Hercules.LLM;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Skills.Eval;

/// <summary>
///     Генератор тестовых кейсов для eval harness.
///
///     - Deterministic fixtures: seeded RNG (seed = skillId hash), воспроизводимы.
///     - LLM-judge cases: temperature=0, seed для reproducibility.
/// </summary>
public sealed class SkillTestGenerator
{
    private readonly ILogger<SkillTestGenerator> _logger;

    public SkillTestGenerator(ILogger<SkillTestGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Генерировать детерминированные fixture'ы на основе описания навыка.
    ///     Seeded по skillId — результат воспроизводим при повторном запуске.
    /// </summary>
    public List<SkillTestCase> GenerateDeterministicFixtures(
        Skill skill,
        int count,
        int? seed = null)
    {
        int rngSeed = seed ?? GetDeterministicSeed(skill.Meta.Id);
        var rng = new Random(rngSeed);

        var fixtures = new List<SkillTestCase>();
        var skillPhrases = skill.Meta.PhraseReceivers;

        string[] genericInputs =
        [
            "что ты можешь?",
            "помоги с задачей",
            "расскажи подробнее",
            "как это работает?",
            "сделай это",
        ];

        string[] variationPrefixes =
        [
            "", "пожалуйста ", "можешь ", "хочу ",
        ];

        for (int i = 0; i < count; i++)
        {
            // Варьируем входной запрос: phrase receiver + variation
            var phrase = skillPhrases.Count > 0
                ? skillPhrases[rng.Next(skillPhrases.Count)]
                : skill.Meta.Name.ToLowerInvariant();

            var prefix = variationPrefixes[rng.Next(variationPrefixes.Length)];
            var input = $"{prefix}{phrase} {genericInputs[i % genericInputs.Length]}";

            // Confidence варьируется
            string[] confidenceLevels = ["high", "medium", "low"];
            var minConfidence = confidenceLevels[rng.Next(confidenceLevels.Length)];

            // 30% тестов — с ExpectedContains на основе description
            string? expectedContains = null;
            if (rng.NextDouble() < 0.3 && !string.IsNullOrWhiteSpace(skill.Description))
            {
                // Берём первое предложение из description как ожидаемую фразу
                var sentences = skill.Description.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (sentences.Length > 0)
                {
                    var sentence = sentences[rng.Next(sentences.Length)].Trim();
                    if (sentence.Length > 5)
                    {
                        expectedContains = sentence.Length > 40
                            ? sentence[..40]
                            : sentence;
                    }
                }
            }

            fixtures.Add(new SkillTestCase
            {
                Name = $"fixture_{i + 1}",
                Input = input,
                ExpectedContains = expectedContains,
                MinConfidence = minConfidence,
                ExpectedMode = "skill",
                JudgedBy = "deterministic"
            });
        }

        _logger.LogDebug(
            "Generated {Count} deterministic fixtures for skill '{SkillId}' (seed={Seed})",
            count, skill.Meta.Id, rngSeed);

        return fixtures;
    }

    /// <summary>
    ///     Генерировать LLM-judge кейсы.
    ///     Judge prompt структурирован для воспроизводимости (temperature=0, seed).
    /// </summary>
    public List<SkillTestCase> GenerateLlmJudgeCases(
        Skill skill,
        int count)
    {
        var cases = new List<SkillTestCase>();

        for (int i = 0; i < count; i++)
        {
            var judgePrompt = BuildJudgePrompt(skill, i);

            cases.Add(new SkillTestCase
            {
                Name = $"llm_judge_{i + 1}",
                Input = $"llm_judge_test_{i + 1}", // placeholder, LLM judge игнорирует реальный input
                JudgedBy = "llm",
                JudgePrompt = judgePrompt,
                ExpectedMode = "llm_judge"
            });
        }

        _logger.LogDebug("Generated {Count} LLM-judge cases for skill '{SkillId}'", count, skill.Meta.Id);
        return cases;
    }

    /// <summary>
    ///     Построить judge prompt для LLM-judge case.
    ///     JudgePrompt объясняет агенту как оценить ответ навыка.
    /// </summary>
    private static string BuildJudgePrompt(Skill skill, int index)
    {
        return $"""
            Ты — строгий judge, оценивающий качество ответа навыка.
            Навык: {skill.Meta.Name}
            Описание: {skill.Meta.Description}
            System prompt: {skill.Prompt}

            Оцени ответ навыка по шкале 0..1:
            - 1.0: полностью соответствует описанию и system prompt
            - 0.5: частично соответствует
            - 0.0: не соответствует или содержит ошибки

            Верни СТРОГО один символ: 1, 0.5 или 0.
            """;
    }

    /// <summary>
    ///     Получить deterministic seed из skill ID.
    ///     Использует MD5 hash → первые 4 байта → int.
    /// </summary>
    private static int GetDeterministicSeed(string skillId)
    {
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(skillId));
        return Math.Abs(BitConverter.ToInt32(hash.AsSpan(0, 4)));
    }
}
