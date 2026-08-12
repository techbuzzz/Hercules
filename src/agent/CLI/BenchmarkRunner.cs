using System.Diagnostics;
using Hercules.Agent;
using Hercules.Storage;
using Hercules.Skills;
using Spectre.Console;

namespace Hercules.CLI;

/// <summary>
///     Benchmark runner: выполняет набор тестовых запросов к агенту,
///     измеряет skill hit rate, latency, tokens, cost, tool success rate, memory growth.
///     Вывод — ASCII-таблица в консоль + CSV-файл (опционально).
/// </summary>
public sealed class BenchmarkRunner
{
    private readonly AgentCore _agent;
    private readonly SkillManager _skills;
    private readonly SqliteSessionStore _sessions;
    private readonly BudgetService _budget;
    private readonly MemoryStore _memory;

    public BenchmarkRunner(
        AgentCore agent,
        SkillManager skills,
        SqliteSessionStore sessions,
        BudgetService budget,
        MemoryStore memory)
    {
        _agent = agent;
        _skills = skills;
        _sessions = sessions;
        _budget = budget;
        _memory = memory;
    }

    /// <summary>
    ///     Запустить benchmark suite. Возвращает true при успехе, false при ошибке.
    /// </summary>
    public async Task<BenchmarkResult> RunAsync(
        IReadOnlyList<BenchmarkPrompt>? prompts = null,
        int warmupRuns = 1,
        CancellationToken ct = default)
    {
        prompts ??= GetDefaultPrompts();

        // Baseline measurements before any benchmark activity
        var memoryBefore = MeasureMemorySize();
        var budgetBefore = await _sessions.GetBudgetSummaryAsync(null, ct);

        // Warmup
        if (warmupRuns > 0)
        {
            AnsiConsole.MarkupLine("[grey]Warmup ({0} run(s))...[/]", warmupRuns);
            for (var i = 0; i < warmupRuns && !ct.IsCancellationRequested; i++)
            {
                await _agent.HandleAsync(_skills.All().Count > 0 ? "привет" : "hello", ct);
            }
        }

        var entries = new List<BenchmarkEntry>();

        AnsiConsole.MarkupLine("[green]Запуск {0} тестовых запросов...[/]\n", prompts.Count);

        foreach (var prompt in prompts)
        {
            if (ct.IsCancellationRequested) break;

            var sw = Stopwatch.StartNew();
            AgentResponse? response = null;
            string? error = null;

            try
            {
                response = await _agent.HandleAsync(prompt.Text, ct);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            sw.Stop();

            entries.Add(new BenchmarkEntry
            {
                PromptName = prompt.Name,
                LatencyMs = sw.ElapsedMilliseconds,
                SkillMatched = response?.UsedSkill is not null,
                SkillName = response?.UsedSkill?.Meta.Name,
                Mode = response?.Mode ?? "error",
                Confidence = response?.Confidence ?? "unknown",
                Error = error
            });
        }

        // Measure after
        var memoryAfter = MeasureMemorySize();
        var budgetAfter = await _sessions.GetBudgetSummaryAsync(null, ct);

        var result = AggregateResults(entries, memoryBefore, memoryAfter, budgetAfter, budgetBefore);
        PrintResults(result);

        return result;
    }

    private BenchmarkResult AggregateResults(
        List<BenchmarkEntry> entries,
        long memoryBeforeBytes,
        long memoryAfterBytes,
        BudgetSummary budgetAfter,
        BudgetSummary budgetBefore)
    {
        var total = entries.Count;
        var errors = entries.Count(e => e.Error is not null);
        var skillHits = entries.Count(e => e.SkillMatched);
        var toolUsage = entries.Count(e => e.Mode == "tool");
        var directUsage = entries.Count(e => e.Mode == "direct");

        return new BenchmarkResult
        {
            TotalRequests = total,
            Errors = errors,
            SuccessRate = total > 0 ? Math.Round((double)(total - errors) / total * 100, 1) : 0,
            SkillHitRate = total > 0 ? Math.Round((double)skillHits / total * 100, 1) : 0,
            ToolUsageRate = total > 0 ? Math.Round((double)toolUsage / total * 100, 1) : 0,
            DirectRate = total > 0 ? Math.Round((double)directUsage / total * 100, 1) : 0,
            AvgLatencyMs = total > 0 ? entries.Average(e => e.LatencyMs) : 0,
            MinLatencyMs = total > 0 ? (int)entries.Min(e => e.LatencyMs) : 0,
            MaxLatencyMs = total > 0 ? (int)entries.Max(e => e.LatencyMs) : 0,
            MedianLatencyMs = total > 0 ? (int)entries.OrderBy(e => e.LatencyMs).ElementAt(total / 2).LatencyMs : 0,
            MemoryGrowthBytes = memoryAfterBytes - memoryBeforeBytes,
            BudgetEntriesAdded = budgetAfter.TotalCalls - budgetBefore.TotalCalls,
            TotalCostUsd = budgetAfter.TotalCostUsd - budgetBefore.TotalCostUsd,
            TotalInputTokens = budgetAfter.TotalInputTokens - budgetBefore.TotalInputTokens,
            TotalOutputTokens = budgetAfter.TotalOutputTokens - budgetBefore.TotalOutputTokens,
            Entries = entries
        };
    }

    private static void PrintResults(BenchmarkResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]═══ Benchmark Results ═══[/]");

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        summaryTable.AddRow("Total requests", result.TotalRequests.ToString());
        summaryTable.AddRow("Errors", result.Errors.ToString());
        summaryTable.AddRow("[green]Success rate[/]", $"{result.SuccessRate}%");
        summaryTable.AddRow("[green]Skill hit rate[/]", $"{result.SkillHitRate}%");
        summaryTable.AddRow("Tool usage rate", $"{result.ToolUsageRate}%");
        summaryTable.AddRow("Direct LLM rate", $"{result.DirectRate}%");
        summaryTable.AddRow("Avg latency", $"{result.AvgLatencyMs:F0} ms");
        summaryTable.AddRow("Min / Max latency", $"{result.MinLatencyMs} / {result.MaxLatencyMs} ms");
        summaryTable.AddRow("Median latency", $"{result.MedianLatencyMs} ms");
        summaryTable.AddRow("Memory growth", FormatBytes(result.MemoryGrowthBytes));
        summaryTable.AddRow("Total cost", $"{result.TotalCostUsd:F6} USD");
        summaryTable.AddRow("Input tokens", result.TotalInputTokens.ToString());
        summaryTable.AddRow("Output tokens", result.TotalOutputTokens.ToString());

        AnsiConsole.Write(summaryTable);

        // Per-request table
        if (result.Entries.Count > 0)
        {
            AnsiConsole.WriteLine();
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]Per-request details[/]")
                .AddColumn("Name")
                .AddColumn("Latency (ms)")
                .AddColumn("Skill?")
                .AddColumn("Skill")
                .AddColumn("Mode")
                .AddColumn("Confidence")
                .AddColumn("Error");

            foreach (var e in result.Entries)
            {
                var skillCell = e.SkillMatched
                    ? $"[green]{(e.SkillName ?? "—")}[/]"
                    : "[grey]—[/]";
                var errorCell = e.Error is not null
                    ? $"[red]{e.Error}[/]"
                    : "[grey]—[/]";
                table.AddRow(
                    e.PromptName,
                    e.LatencyMs.ToString(),
                    e.SkillMatched ? "✓" : "✗",
                    skillCell,
                    e.Mode,
                    e.Confidence,
                    errorCell);
            }

            AnsiConsole.Write(table);
        }
    }

    private static long MeasureMemorySize()
    {
        return GC.GetTotalAllocatedBytes(true);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>
    ///     Набор тестовых промптов по умолчанию.
    ///     Один навык покрывает все основные категории.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> GetDefaultPrompts() =>
    [
        new("greeting", "привет"),
        new("weather-skill", "какая погода в москве"),
        new("code-skill", "напиши функцию сортировки"),
        new("general-query", "что такое самообучение агентов"),
        new("memory-query", "что ты знаешь обо мне"),
    ];
}

/// <summary>Результат одного benchmark-запуска.</summary>
public sealed class BenchmarkResult
{
    public int TotalRequests { get; init; }
    public int Errors { get; init; }
    public double SuccessRate { get; init; }
    public double SkillHitRate { get; init; }
    public double ToolUsageRate { get; init; }
    public double DirectRate { get; init; }
    public double AvgLatencyMs { get; init; }
    public int MinLatencyMs { get; init; }
    public int MaxLatencyMs { get; init; }
    public int MedianLatencyMs { get; init; }
    public long MemoryGrowthBytes { get; init; }
    public int BudgetEntriesAdded { get; init; }
    public decimal TotalCostUsd { get; init; }
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public IReadOnlyList<BenchmarkEntry> Entries { get; init; } = [];
}

/// <summary>Один benchmark-промпт.</summary>
public sealed record BenchmarkPrompt(string Name, string Text);

/// <summary>Результат одного запроса в benchmark.</summary>
public sealed class BenchmarkEntry
{
    public required string PromptName { get; init; }
    public required long LatencyMs { get; init; }
    public required bool SkillMatched { get; init; }
    public string? SkillName { get; init; }
    public required string Mode { get; init; }
    public required string Confidence { get; init; }
    public string? Error { get; init; }
}
