namespace Hercules.WebApi.Contracts;

/// <summary>One agent's answer as stored in a consensus session.</summary>
public sealed class ConsensusAnswerDto
{
    public required string AgentName { get; init; }
    public required string ConnectionId { get; init; }
    public required string Answer { get; init; }
}

/// <summary>A stored consensus round (Stage 7.8). Answers are kept verbatim.</summary>
public sealed class ConsensusSessionDto
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> SelectedAgents { get; init; }
    public required IReadOnlyList<ConsensusAnswerDto> Responses { get; init; }

    /// <summary><c>manual</c> or <c>llm-judge</c>.</summary>
    public required string AggregationMode { get; init; }

    public string? Result { get; init; }
    public string? JudgeRationale { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class ConsensusSessionListDto
{
    public required int Count { get; init; }
    public required IReadOnlyList<ConsensusSessionDto> Items { get; init; }
}

/// <summary>Request to record a consensus round.</summary>
public sealed class SaveConsensusSessionRequest
{
    public string Prompt { get; set; } = "";
    public List<string> SelectedAgents { get; set; } = [];
    public List<ConsensusAnswerDto> Responses { get; set; } = [];
    public string AggregationMode { get; set; } = "manual";
    public string? Result { get; set; }
    public string? JudgeRationale { get; set; }
}