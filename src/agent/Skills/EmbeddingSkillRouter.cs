using Hercules.Agent;
using Hercules.LLM;
using Hercules.Storage;

namespace Hercules.Skills;

/// <summary>
///     Результат семантической маршрутизации: навык + оценка сходства.
/// </summary>
public readonly record struct SemanticRouteResult(Skill? MatchedSkill, double Score, string Method)
{
    public bool IsSkill => MatchedSkill is not null;
}

/// <summary>
///     Семантический маршрутизатор навыков: ранжирует навыки по embedding-сходству
///     запроса с описанием навыка и его фразами-приёмниками.
///     В отличие от SkillRouter (keyword-matching), понимает синонимы и парафразы.
///     Гибридный подход: если embedding-сходство низкое — fallback на keyword-matching.
/// </summary>
public sealed class EmbeddingSkillRouter
{
    private readonly IEmbeddingProvider _embedder;
    private readonly SkillManager _skills;
    private readonly Dictionary<string, float[]> _skillEmbeddings = new(StringComparer.OrdinalIgnoreCase);
    private List<string>? _cachedSkillIds;

    /// <summary>Минимальный порог cosine-similarity для семантического матча (0..1).</summary>
    public double SimilarityThreshold { get; set; } = 0.35;

    /// <summary>Использовать keyword-matching как fallback, если embedding < порога.</summary>
    public bool UseKeywordFallback { get; set; } = true;

    public EmbeddingSkillRouter(IEmbeddingProvider embedder, SkillManager skills)
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
    }

    /// <summary>
    ///     Маршрутизировать запрос: найти подходящий навык по embedding-сходству.
    ///     Возвращает лучший навык и метод маршрутизации ("embedding" | "keyword" | "none").
    /// </summary>
    public async Task<SemanticRouteResult> RouteAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SemanticRouteResult(null, 0, "none");
        }

        // Получаем embedding запроса
        var queryEmbedding = await _embedder.EmbedAsync(input, ct);
        if (IsZeroVector(queryEmbedding))
        {
            return UseKeywordFallback
                ? KeywordFallback(input)
                : new SemanticRouteResult(null, 0, "none");
        }

        // Загружаем навыки и строим кэш embeddings (если изменился список навыков)
        var allSkills = _skills.All();
        RefreshCacheIfNeeded(allSkills);

        // Ранжируем по cosine-similarity
        Skill? best = null;
        var bestScore = 0.0;
        foreach (var skill in allSkills)
        {
            if (!_skillEmbeddings.TryGetValue(skill.Meta.Id, out var skillEmb))
            {
                continue;
            }

            var sim = CosineSimilarity(queryEmbedding, skillEmb);
            if (sim > bestScore)
            {
                bestScore = sim;
                best = skill;
            }
        }

        if (best is not null && bestScore >= SimilarityThreshold)
        {
            return new SemanticRouteResult(best, bestScore, "embedding");
        }

        // Fallback на keyword-matching
        if (UseKeywordFallback)
        {
            var kwResult = KeywordFallback(input);
            if (kwResult.IsSkill)
            {
                return new SemanticRouteResult(kwResult.MatchedSkill, kwResult.Score, "keyword");
            }
        }

        return new SemanticRouteResult(null, bestScore, "none");
    }

    /// <summary>Принудительно перестроить кэш embeddings при следующем вызове RouteAsync.</summary>
    public void InvalidateCache()
    {
        _cachedSkillIds = null;
        _skillEmbeddings.Clear();
    }

    private void RefreshCacheIfNeeded(List<Skill> skills)
    {
        var currentIds = skills.Select(s => s.Meta.Id).OrderBy(id => id).ToList();
        if (_cachedSkillIds is not null && _cachedSkillIds.SequenceEqual(currentIds))
        {
            return; // Кэш актуален
        }

        _skillEmbeddings.Clear();
        foreach (var skill in skills)
        {
            // Embedding навыка = конкатенация имени, описания и фраз-приёмников
            var skillText = $"{skill.Meta.Name} {skill.Meta.Description} {string.Join(" ", skill.Meta.PhraseReceivers)}";
            var emb = _embedder.EmbedAsync(skillText).GetAwaiter().GetResult();
            _skillEmbeddings[skill.Meta.Id] = emb;
        }
        _cachedSkillIds = currentIds;
    }

    private SemanticRouteResult KeywordFallback(string input)
    {
        var normalized = Hercules.Agent.SkillRouter.Normalize(input);
        Skill? best = null;
        var bestScore = 0;

        foreach (Skill skill in _skills.All())
        {
            var score = skill.Meta.PhraseReceivers.Count(receiver =>
                !string.IsNullOrWhiteSpace(receiver) &&
                normalized.Contains(Hercules.Agent.SkillRouter.Normalize(receiver), StringComparison.Ordinal));

            if (score > bestScore)
            {
                bestScore = score;
                best = skill;
            }
        }

        return bestScore > 0
            ? new SemanticRouteResult(best, bestScore, "keyword")
            : new SemanticRouteResult(null, 0, "none");
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }

    private static bool IsZeroVector(float[] v)
    {
        foreach (var x in v)
        {
            if (x != 0) return false;
        }
        return true;
    }
}