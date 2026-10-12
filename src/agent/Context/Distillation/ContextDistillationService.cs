using System.Text;
using System.Text.RegularExpressions;
using Hercules.Config;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Context.Distillation;

/// <summary>
///     Детерминированная иерархическая дистилляция контекста (task_102).
///     <para>
///         Без вызова LLM: использует sentence scoring + n-gram frequency для
///         reproducible summaries и key-facts. Это даёт:
///         <list type="bullet">
///             <item><description>Offline-capable summary (агент работает без LLM-провайдера).</description></item>
///             <item><description>Reproducible test fixtures (одинаковый input → одинаковый output).</description></item>
///             <item><description>Минимальный hot-path overhead (CPU-only, без сетевых вызовов).</description></item>
///         </list>
///     </para>
///     <para>
///         Tier-ы (иерархия):
///         <list type="number">
///             <item><description><b>Recent</b> — последние <c>RecentRawCount</c> сообщений, raw (token estimate = len/4).</description></item>
///             <item><description><b>Summary</b> — older сообщения, сводка по <c>SummaryInterval</c> сообщений (одна запись на интервал).</description></item>
///             <item><description><b>Ancient</b> — самые старые, top-N n-gram (1-3) с фильтрацией стоп-слов.</description></item>
///         </list>
///     </para>
/// </summary>
public sealed class ContextDistillationService
{
    // Минимальный английский/русский стоп-лист для n-gram фильтра.
    // Достаточно для MVP; в проде можно заменить на полноценный список.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","if","then","else","is","are","was","were","be","been","being",
        "i","you","he","she","it","we","they","me","him","her","us","them","my","your","his","its","our","their",
        "this","that","these","those","of","in","on","at","to","for","with","from","by","as","into","about",
        "not","no","yes","do","does","did","done","doing","have","has","had","having",
        "что","это","как","так","и","в","на","с","по","для","не","да","из","за","к","от","у","о","об","до",
        "он","она","оно","мы","вы","они","я","ты","мой","твой","его","её","наш","ваш","их",
        "при","ещё","уже","или","но","если","тогда","когда","где","кто","чтобы","потому","что","ли"
    };

    private readonly IDistillationStore _store;
    private readonly SqliteSessionStore _sessions;
    private readonly ILogger<ContextDistillationService> _log;

    public ContextDistillationService(
        IDistillationStore store,
        SqliteSessionStore sessions,
        ILogger<ContextDistillationService> log)
    {
        _store = store;
        _sessions = sessions;
        _log = log;
    }

    /// <summary>
    ///     Запустить дистилляцию для сессии. Возвращает структурированный результат
    ///     (recent count, созданные summaries, extracted key facts, token savings).
    /// </summary>
    /// <param name="sessionId">ID сессии.</param>
    /// <param name="cfg">Конфигурация дистилляции.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<DistillationResult> DistillAsync(
        string sessionId,
        DistillationConfig cfg,
        CancellationToken ct = default)
    {
        if (cfg.Mode == DistillationMode.Off)
            return new DistillationResult(sessionId, 0, 0, 0, 0, 0, "");

        // 1. Fetch interactions in chronological order.
        var interactions = await _sessions.GetSessionInteractionsAsync(sessionId, limit: 500, ct);
        if (interactions.Count == 0)
            return new DistillationResult(sessionId, 0, 0, 0, 0, 0, "");

        // 2. Build ConversationMessage list with 0-based index.
        var messages = new List<ConversationMessage>(interactions.Count);
        for (int i = 0; i < interactions.Count; i++)
        {
            var it = interactions[i];
            messages.Add(new ConversationMessage(
                Index: i,
                SessionId: it.SessionId,
                Input: it.Input ?? "",
                Output: it.Output ?? "",
                Confidence: it.Confidence ?? "medium",
                CreatedAt: it.CreatedAt));
        }

        // 3. Split into tiers.
        var recent = TakeRecent(messages, cfg.RecentRawCount);
        var older = TakeOlder(messages, cfg.RecentRawCount);
        var ancient = TakeAncient(messages, cfg.RecentRawCount, cfg.SummaryInterval);

        // 4. Generate summaries for "older" tier, grouped by SummaryInterval.
        var summaries = BuildSummaries(older, cfg, sessionId, ct);
        foreach (var s in summaries)
            await _store.SaveSummaryAsync(s, ct);

        // 5. Extract key facts from "ancient" tier.
        int keyFactsCreated = 0;
        if (cfg.KeyFactsExtraction && ancient.Count > 0)
        {
            var facts = ExtractKeyFacts(ancient, cfg.MaxAncientFacts);
            if (facts.Count > 0)
            {
                await _store.SaveKeyFactsAsync(sessionId, facts, ct);
                keyFactsCreated = facts.Count;
            }
        }

        // 6. Compute token savings.
        int tokensBefore = messages.Sum(m => EstimateTokens(m.Input) + EstimateTokens(m.Output));
        int tokensAfter = recent.Sum(m => EstimateTokens(m.Input) + EstimateTokens(m.Output))
            + summaries.Sum(s => s.TokenEstimate)
            + (cfg.KeyFactsExtraction
                ? Math.Min(cfg.MaxAncientFacts, keyFactsCreated) * 8 // грубая оценка: ~8 токенов на fact
                : 0);

        // 7. Assemble markdown summary.
        var markdown = RenderMarkdown(sessionId, recent, summaries, cfg);

        _log.LogInformation(
            "[Distillation] session={Session} msgs={Msgs} recent={Recent} summaries={Sum} facts={Facts} tokens {Before}→{After}",
            sessionId, messages.Count, recent.Count, summaries.Count, keyFactsCreated, tokensBefore, tokensAfter);

        return new DistillationResult(
            SessionId: sessionId,
            RecentCount: recent.Count,
            SummariesCreated: summaries.Count,
            KeyFactsExtracted: keyFactsCreated,
            TokensBefore: tokensBefore,
            TokensAfter: tokensAfter,
            SummaryMarkdown: markdown);
    }

    /// <summary>
    ///     Получить текущую сводку сессии (markdown) — используется для <c>GET /api/context/summary</c>.
    ///     Возвращает пустую строку, если для сессии нет ни recent, ни summaries.
    /// </summary>
    public async Task<string> GetSummaryAsync(
        string sessionId,
        DistillationConfig cfg,
        int maxTokens,
        CancellationToken ct = default)
    {
        // 1. Pull existing summaries from store.
        var stored = await _store.GetSummariesAsync(sessionId, ct);
        var storedFacts = cfg.KeyFactsExtraction
            ? await _store.GetKeyFactsAsync(sessionId, cfg.MaxAncientFacts, ct)
            : Array.Empty<KeyFact>();

        // 2. Pull recent raw from interactions.
        var interactions = await _sessions.GetSessionInteractionsAsync(sessionId, limit: cfg.RecentRawCount, ct);
        var recent = new List<ConversationMessage>(interactions.Count);
        int startIdx = 0;
        // If we have stored summaries, recent should not overlap. We assume the
        // caller passes the full session messages; the last N before the latest
        // summary's ToIndex are recent.
        if (stored.Count > 0)
        {
            var latestToIndex = stored[^1].ToIndex;
            startIdx = latestToIndex + 1;
        }
        for (int i = 0; i < interactions.Count; i++)
        {
            var it = interactions[i];
            recent.Add(new ConversationMessage(
                Index: startIdx + i,
                SessionId: it.SessionId,
                Input: it.Input ?? "",
                Output: it.Output ?? "",
                Confidence: it.Confidence ?? "medium",
                CreatedAt: it.CreatedAt));
        }

        return RenderMarkdown(sessionId, recent, stored, storedFacts, maxTokens);
    }

    // -- Tier splitting -----------------------------------------------------

    private static List<ConversationMessage> TakeRecent(List<ConversationMessage> all, int recentCount)
    {
        if (recentCount <= 0 || all.Count <= recentCount)
            return new List<ConversationMessage>();
        return all.GetRange(all.Count - recentCount, recentCount);
    }

    private static List<ConversationMessage> TakeOlder(List<ConversationMessage> all, int recentCount)
    {
        int olderEnd = Math.Max(0, all.Count - recentCount);
        if (olderEnd == 0) return new List<ConversationMessage>();
        return all.GetRange(0, olderEnd);
    }

    private static List<ConversationMessage> TakeAncient(
        List<ConversationMessage> all, int recentCount, int summaryInterval)
    {
        // Ancient = older than (recent + summaryInterval * summary batches)
        // For MVP: ancient = first half of older tier (everything before the
        // last `summaryInterval * 2` messages of older).
        int olderEnd = Math.Max(0, all.Count - recentCount);
        int ancientCutoff = Math.Max(0, olderEnd - Math.Max(summaryInterval * 2, 4));
        if (ancientCutoff == 0) return new List<ConversationMessage>();
        return all.GetRange(0, ancientCutoff);
    }

    // -- Summary generation -------------------------------------------------

    private static List<DistillationSummary> BuildSummaries(
        List<ConversationMessage> older,
        DistillationConfig cfg,
        string sessionId,
        CancellationToken ct)
    {
        if (older.Count == 0) return new List<DistillationSummary>();
        var results = new List<DistillationSummary>();
        int interval = Math.Max(1, cfg.SummaryInterval);
        int batches = (older.Count + interval - 1) / interval;

        // First message index in `older` equals 0 (since older = all[..olderEnd]).
        // To produce FromIndex/ToIndex relative to the FULL session, we use the
        // message.Index field directly (it stores the 0-based position in the
        // full session).
        for (int b = 0; b < batches; b++)
        {
            int from = b * interval;
            int to = Math.Min(from + interval - 1, older.Count - 1);
            var batch = older.GetRange(from, to - from + 1);

            string summary = SummarizeBatch(batch);
            int tokens = EstimateTokens(summary);
            results.Add(new DistillationSummary(
                Id: 0,
                SessionId: sessionId,
                FromIndex: batch[0].Index,
                ToIndex: batch[^1].Index,
                Summary: summary,
                MessageCount: batch.Count,
                TokenEstimate: tokens,
                CreatedAt: DateTime.UtcNow));
        }
        return results;
    }

    /// <summary>
    ///     Детерминированная сводка батча: выбираем 2-3 ключевых предложения по
    ///     simple scoring (длина + плотность значимых слов), плюс topic words.
    /// </summary>
    internal static string SummarizeBatch(IReadOnlyList<ConversationMessage> batch)
    {
        if (batch.Count == 0) return "";

        // 1. Compute word frequency across batch (excluding stop words).
        var wordFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in batch)
        {
            foreach (var w in Tokenize(m.Input + " " + m.Output))
            {
                if (StopWords.Contains(w)) continue;
                if (w.Length < 3) continue;
                wordFreq[w] = wordFreq.GetValueOrDefault(w) + 1;
            }
        }

        // 2. Pick top-5 topic words.
        var topics = wordFreq
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        // 3. Split each message into sentences; score by topic density.
        var scoredSentences = new List<(string text, double score)>();
        foreach (var m in batch)
        {
            var sentences = SplitSentences(m.Input + " " + m.Output);
            foreach (var s in sentences)
            {
                if (s.Length < 8) continue;
                double score = ScoreSentence(s, topics);
                if (score > 0)
                    scoredSentences.Add((s.Trim(), score));
            }
        }

        // 4. Top-3 sentences, ordered by source message index then by score.
        var topSentences = scoredSentences
            .OrderByDescending(x => x.score)
            .Take(3)
            .Select(x => x.text)
            .ToList();

        // 5. Render as markdown bullets.
        var sb = new StringBuilder();
        if (topics.Count > 0)
            sb.AppendLine($"**Topics:** {string.Join(", ", topics)}");
        if (topSentences.Count == 0)
        {
            // fallback: first 200 chars of first message
            var first = (batch[0].Input + " " + batch[0].Output).Trim();
            if (first.Length > 200) first = first[..200] + "...";
            sb.AppendLine("- " + first);
        }
        else
        {
            foreach (var s in topSentences)
                sb.AppendLine("- " + s);
        }
        return sb.ToString().TrimEnd();
    }

    // -- Key-facts extraction ----------------------------------------------

    /// <summary>
    ///     Извлечь top-N key-facts из ancient сообщений.
    ///     Алгоритм: n-gram frequency (1-3) + фильтрация стоп-слов + dedup по
    ///     пересечению. Возвращает дедуплицированный список.
    /// </summary>
    internal static List<KeyFact> ExtractKeyFacts(
        IReadOnlyList<ConversationMessage> ancient, int maxFacts)
    {
        if (ancient.Count == 0 || maxFacts <= 0) return new List<KeyFact>();

        var ngramFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ngramFirstSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in ancient)
        {
            var text = m.Input + " " + m.Output;
            var tokens = Tokenize(text).Where(t => !StopWords.Contains(t) && t.Length >= 3).ToList();

            // 1-gram, 2-gram, 3-gram.
            for (int n = 1; n <= 3; n++)
            {
                for (int i = 0; i + n <= tokens.Count; i++)
                {
                    var gram = string.Join(" ", tokens.Skip(i).Take(n));
                    ngramFreq[gram] = ngramFreq.GetValueOrDefault(gram) + 1;
                    if (!ngramFirstSeen.ContainsKey(gram) || m.CreatedAt < ngramFirstSeen[gram])
                        ngramFirstSeen[gram] = m.CreatedAt;
                }
            }
        }

        // Score: frequency * (1 + log(n)) bonus for longer n-grams, normalise by message count.
        int totalMessages = ancient.Count;
        var candidates = ngramFreq
            .Where(kv => kv.Value >= Math.Max(1, totalMessages / 5)) // appears in ≥20% of ancient
            .Select(kv => new
            {
                Text = kv.Key,
                Score = kv.Value * (1.0 + 0.1 * kv.Key.Split(' ').Length),
                Count = kv.Value,
                FirstSeen = ngramFirstSeen[kv.Key]
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Text, StringComparer.OrdinalIgnoreCase)
            .Take(maxFacts * 3) // over-fetch, then dedup
            .ToList();

        // Dedup: drop n-gram if a longer n-gram containing it is already selected.
        var selected = new List<KeyFact>();
        var selectedTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            bool isSubstring = selected.Any(s => s.FactText.Contains(c.Text, StringComparison.OrdinalIgnoreCase));
            if (isSubstring) continue;
            // Also drop if current contains a selected shorter n-gram.
            bool containsSelected = selected.Any(s => c.Text.Contains(s.FactText, StringComparison.OrdinalIgnoreCase));
            if (containsSelected)
            {
                // Replace the shorter one with the longer.
                var shorterIdx = selected.FindIndex(s => c.Text.Contains(s.FactText, StringComparison.OrdinalIgnoreCase));
                if (shorterIdx >= 0)
                {
                    selected.RemoveAt(shorterIdx);
                    selectedTexts.RemoveWhere(t => t.Length < c.Text.Length);
                }
            }
            selected.Add(new KeyFact(c.Text, c.Score, c.Count, c.FirstSeen));
            selectedTexts.Add(c.Text);
            if (selected.Count >= maxFacts) break;
        }
        return selected;
    }

    // -- Markdown rendering -------------------------------------------------

    private static string RenderMarkdown(
        string sessionId,
        IReadOnlyList<ConversationMessage> recent,
        IReadOnlyList<DistillationSummary> summaries,
        DistillationConfig cfg)
    {
        var storedFacts = cfg.KeyFactsExtraction
            ? new List<KeyFact>() // We don't pull from store here (DistillAsync path only).
            : new List<KeyFact>();
        return RenderMarkdown(sessionId, recent, summaries, storedFacts, maxTokens: int.MaxValue);
    }

    private static string RenderMarkdown(
        string sessionId,
        IReadOnlyList<ConversationMessage> recent,
        IReadOnlyList<DistillationSummary> summaries,
        IReadOnlyList<KeyFact> keyFacts,
        int maxTokens)
    {
        // Empty session — no summaries, no key-facts, no recent — return empty string.
        // Caller can distinguish "no data" from "header-only" via the length check.
        if (summaries.Count == 0 && keyFacts.Count == 0 && recent.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine($"# Distilled context for session `{sessionId}`");
        sb.AppendLine();

        if (summaries.Count > 0)
        {
            sb.AppendLine($"## Summaries ({summaries.Count})");
            foreach (var s in summaries)
            {
                sb.AppendLine($"### Range [{s.FromIndex}..{s.ToIndex}] (msgs={s.MessageCount}, ~{s.TokenEstimate} tok)");
                sb.AppendLine(s.Summary);
                sb.AppendLine();
            }
        }

        if (keyFacts.Count > 0)
        {
            sb.AppendLine($"## Key facts ({keyFacts.Count})");
            foreach (var f in keyFacts)
                sb.AppendLine($"- {f.FactText} _(×{f.SourceCount}, score={f.Score:F1})_");
            sb.AppendLine();
        }

        if (recent.Count > 0)
        {
            sb.AppendLine($"## Recent ({recent.Count})");
            int idx = 0;
            foreach (var m in recent)
            {
                sb.AppendLine($"### [{m.Index}] {m.CreatedAt:yyyy-MM-dd HH:mm} (conf={m.Confidence})");
                if (!string.IsNullOrWhiteSpace(m.Input))
                    sb.AppendLine("**User:** " + m.Input);
                if (!string.IsNullOrWhiteSpace(m.Output))
                {
                    var out5 = m.Output.Length > 400 ? m.Output[..400] + "..." : m.Output;
                    sb.AppendLine("**Assistant:** " + out5);
                }
                sb.AppendLine();
                idx++;
            }
        }

        if (maxTokens > 0 && maxTokens < int.MaxValue)
        {
            int totalTokens = EstimateTokens(sb.ToString());
            if (totalTokens > maxTokens)
            {
                // Truncate at the last paragraph boundary that fits.
                string text = sb.ToString();
                int lo = 0, hi = text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (EstimateTokens(text[..mid]) <= maxTokens) lo = mid;
                    else hi = mid - 1;
                }
                return text[..lo] + $"\n\n_(truncated to ~{maxTokens} tokens)_";
            }
        }
        return sb.ToString().TrimEnd();
    }

    // -- Helpers ------------------------------------------------------------

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, text.Length / 4);
    }

    private static readonly Regex SentenceSplitter = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return SentenceSplitter.Split(text).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static readonly Regex WordSplitter = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (Match m in WordSplitter.Matches(text))
        {
            if (m.Length == 0) continue;
            yield return m.Value.ToLowerInvariant();
        }
    }

    private static double ScoreSentence(string sentence, IReadOnlyList<string> topics)
    {
        if (topics.Count == 0) return sentence.Length / 50.0;
        var sentLower = sentence.ToLowerInvariant();
        int hits = topics.Count(t => sentLower.Contains(t, StringComparison.OrdinalIgnoreCase));
        return hits * 2.0 + Math.Min(2.0, sentence.Length / 100.0);
    }
}
