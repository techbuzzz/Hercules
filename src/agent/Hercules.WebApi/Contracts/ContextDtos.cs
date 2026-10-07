namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shapes for the context / distillation endpoints (Stage 6.6).
/// Named so Studio's generated client can describe them.
/// </summary>
public sealed class ContextBudgetDto
{
    public required int MaxTokens { get; init; }
    public required int UsedTokens { get; init; }
    public required int RemainingTokens { get; init; }

    /// <summary>Used share of the budget, one decimal. 0 when no budget is configured.</summary>
    public required double BudgetUsedPct { get; init; }
}

public sealed class ContextSummaryDto
{
    public required string SessionId { get; init; }
    public required string Mode { get; init; }
    public required string Summary { get; init; }
    public required bool Empty { get; init; }
}

public sealed class DistillRequest
{
    public string? SessionId { get; set; }

    /// <summary>Optional per-call override: <c>off</c>, <c>auto</c> or <c>manual</c>.</summary>
    public string? Mode { get; set; }
}

/// <summary>Result of a distillation run.</summary>
public sealed class ContextDistillResultDto
{
    public required string SessionId { get; init; }
    public required int RecentCount { get; init; }
    public required int SummariesCreated { get; init; }
    public required int KeyFactsExtracted { get; init; }
    public required int TokensBefore { get; init; }
    public required int TokensAfter { get; init; }
    public required double TokenSavingsPct { get; init; }
    public required string SummaryMarkdown { get; init; }
}