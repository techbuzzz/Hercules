using System.Text;
using Hercules.Config;
using Hercules.Context.Summarizer;
using Hercules.Memory.Layers;
using Hercules.Skills;
using Hercules.Storage;
using Microsoft.Extensions.Logging;

namespace Hercules.Context;

/// <summary>
///     Контекстный билдер: собирает память, tool schemas, episodes в рамках token-бюджета.
///     Приоритизирует high-confidence facts, фильтрует sensitive данные,
///     сжимает tool traces в episodic memory.
///
///     Task 027 — Context Assembly.
/// </summary>
public sealed class ContextBuilder : IContextBuilder
{
    private readonly LayeredMemoryManager? _memory;
    private readonly ITraceSummarizer? _traceSummarizer;
    private readonly ContextConfig _cfg;
    private readonly ILogger<ContextBuilder> _logger;

    // Current session budget tracking
    private ContextBudget _currentBudget;

    public ContextBuilder(
        LayeredMemoryManager? memory,
        ContextConfig cfg,
        ILogger<ContextBuilder> logger,
        ITraceSummarizer? traceSummarizer = null)
    {
        _memory = memory;
        _traceSummarizer = traceSummarizer;
        _cfg = cfg;
        _logger = logger;

        var maxTokens = cfg.MaxContextTokens;
        _currentBudget = new ContextBudget(maxTokens, 0, maxTokens);
    }

    public async Task<ContextAssembly> BuildContextAsync(
        string input,
        string sessionId,
        Skill? skill,
        CancellationToken ct = default)
    {
        if (!_cfg.Enabled || _memory is null)
        {
            // Fallback: empty context
            return new ContextAssembly("", _currentBudget, 0, false);
        }

        var items = new List<ContextItem>();
        var availableTokens = _cfg.MaxContextTokens - _cfg.SystemPromptOverheadTokens;
        if (availableTokens < 0) availableTokens = _cfg.MaxContextTokens;

        // 1. Durable facts — High importance first
        var facts = await _memory.BuildContextBlockAsync(ct);
        items.AddRange(ParseFactsFromContext(facts, ImportanceLevel.High));

        // 2. Episodes
        var episodes = _memory?.EpisodicStore is not null
            ? await _memory.EpisodicStore.GetRecentEpisodesAsync(_cfg.MaxEpisodesInContext, ct)
            : Array.Empty<Episode>();
        foreach (var ep in episodes)
        {
            if (ShouldRedact(ep.Entry)) continue;
            var tokens = EstimateTokens(ep.Summary);
            items.Add(new ContextItem(
                ContextItemType.Episode,
                $"[{ep.CreatedAt:yyyy-MM-dd}] {ep.Summary.Trim()}",
                tokens,
                ImportanceLevel.Medium,
                "episodic",
                ep.Entry.Tags));
        }

        // 3. Working memory (from LayeredMemoryManager facade)
        var workingCtx = await _memory.BuildContextBlockAsync(ct);
        items.AddRange(ParseWorkingFromContext(workingCtx, ImportanceLevel.Medium));

        // 5. Sort by importance (High → Medium → Low), then by token size (smaller first)
        var sorted = items
            .Where(i => !IsSensitive(i))
            .OrderByDescending(i => i.Importance)
            .ThenBy(i => i.TokenEstimate)
            .ToList();

        // 6. Assemble within token budget
        var sb = new StringBuilder();
        var usedTokens = 0;
        var truncated = false;
        var factCount = 0;

        foreach (var item in sorted)
        {
            if (factCount >= _cfg.MaxFactsInContext && item.Type == ContextItemType.DurableFact)
            {
                truncated = true;
                continue;
            }

            if (usedTokens + item.TokenEstimate > availableTokens)
            {
                truncated = true;
                continue;
            }

            var sectionHeader = SectionHeader(item.Type);
            sb.Append(sectionHeader);
            sb.AppendLine(item.Content.Trim());
            sb.AppendLine();
            usedTokens += item.TokenEstimate + EstimateTokens(sectionHeader);
            factCount++;

            if (factCount >= _cfg.MaxFactsInContext + _cfg.MaxEpisodesInContext + 5)
            {
                truncated = true;
                break;
            }
        }

        var contextBlock = sb.ToString();
        _currentBudget = new ContextBudget(
            availableTokens,
            usedTokens,
            availableTokens - usedTokens);

        return new ContextAssembly(
            contextBlock,
            _currentBudget,
            factCount,
            truncated);
    }

    public async Task<bool> CompressTraceAsync(
        IReadOnlyList<ToolTraceEntry> trace,
        string sessionId,
        CancellationToken ct = default)
    {
        if (trace.Count < _cfg.CompressionThreshold)
        {
            _logger.LogDebug("[ContextBuilder] Trace count {Count} < threshold {Threshold}, skipping compression",
                trace.Count, _cfg.CompressionThreshold);
            return false;
        }

        if (_memory?.EpisodicStore is null)
        {
            _logger.LogWarning("[ContextBuilder] No episodic store available for trace compression");
            return false;
        }

        var summary = _traceSummarizer?.Summarize(trace) ?? DefaultSummarize(trace);
        var entry = new MemoryEntry("trace_compression", MemoryConfidence.Medium)
        {
            Tags = new List<string> { "trace", "compressed", $"tools:{trace.Count}" }
        };

        await _memory!.EpisodicStore.AppendEpisodeAsync(sessionId, summary, entry, ct);
        _logger.LogInformation("[ContextBuilder] Compressed {Count} tool calls into episodic memory for session {SessionId}",
            trace.Count, sessionId);
        return true;
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, text.Length / 4);
    }

    public ContextBudget GetCurrentBudget() => _currentBudget;

    private IEnumerable<ContextItem> ParseFactsFromContext(string contextBlock, ImportanceLevel defaultImportance)
    {
        // Parse facts from the layered memory context block
        // Format: "- [confidence] key: value"
        var lines = contextBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inFactsSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("==="))
            {
                inFactsSection = trimmed.Contains("УСТОЙЧИВЫЕ ФАКТЫ") || trimmed.Contains("PERSISTENT FACTS");
                continue;
            }

            if (!inFactsSection) continue;
            if (!trimmed.StartsWith("- [")) continue;

            // Parse "- [Confidence] key: value"
            var bracketEnd = trimmed.IndexOf(']');
            if (bracketEnd < 0) continue;

            var conf = trimmed[2..bracketEnd].Trim().ToLowerInvariant();
            var rest = trimmed[(bracketEnd + 1)..].TrimStart(':').Trim();
            var colonIdx = rest.IndexOf(':');
            if (colonIdx < 0) continue;

            var importance = conf switch
            {
                "high" => ImportanceLevel.High,
                "medium" => ImportanceLevel.Medium,
                _ => ImportanceLevel.Low
            };

            var key = rest[..colonIdx].Trim();
            var value = rest[(colonIdx + 1)..].Trim();

            yield return new ContextItem(
                ContextItemType.DurableFact,
                $"{key}: {value}",
                EstimateTokens($"{key}: {value}"),
                importance,
                "durable_fact",
                new List<string>());
        }
    }

    private IEnumerable<ContextItem> ParseWorkingFromContext(string contextBlock, ImportanceLevel defaultImportance)
    {
        var lines = contextBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inWorkingSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("==="))
            {
                inWorkingSection = trimmed.Contains("РАБОЧАЯ ПАМЯТЬ") || trimmed.Contains("WORKING MEMORY");
                continue;
            }

            if (!inWorkingSection) continue;
            if (!trimmed.StartsWith("- [") && !trimmed.StartsWith("[")) continue;

            var bracketEnd = trimmed.IndexOf(']');
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = bracketEnd >= 0
                ? trimmed[(bracketEnd + 1)..colonIdx].TrimStart(':').Trim()
                : trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            yield return new ContextItem(
                ContextItemType.WorkingMemory,
                $"{key}: {value}",
                EstimateTokens($"{key}: {value}"),
                defaultImportance,
                "working_memory",
                new List<string>());
        }
    }

    private static string SectionHeader(ContextItemType type) => type switch
    {
        ContextItemType.DurableFact => "=== УСТОЙЧИВЫЕ ФАКТЫ ===\n",
        ContextItemType.WorkingMemory => "=== РАБОЧАЯ ПАМЯТЬ ===\n",
        ContextItemType.Episode => "=== КОНТЕКСТ ПРОШЛЫХ СЕССИЙ ===\n",
        ContextItemType.ToolSchema => "=== ДОСТУПНЫЕ ИНСТРУМЕНТЫ ===\n",
        ContextItemType.SkillPrompt => "",
        ContextItemType.RequestContext => "",
        _ => ""
    };

    private bool IsSensitive(ContextItem item) =>
        item.Tags.Contains("sensitive", StringComparer.OrdinalIgnoreCase) ||
        item.Tags.Contains("restricted", StringComparer.OrdinalIgnoreCase);

    private static bool ShouldRedact(MemoryEntry entry)
    {
        return entry.Sensitivity is MemorySensitivity.Sensitive or MemorySensitivity.Restricted;
    }

    private static string DefaultSummarize(IReadOnlyList<ToolTraceEntry> trace)
    {
        var tools = trace
            .GroupBy(t => t.ToolName)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();
        var totalMs = trace.Sum(t => t.DurationMs);
        var successes = trace.Count(t => t.Success);
        return $"[Compressed trace] {trace.Count} calls: {string.Join(", ", tools)}. " +
               $"Duration: {totalMs}ms. Success rate: {successes}/{trace.Count}.";
    }
}

