namespace Hercules.Skills.Eval;

/// <summary>
///     Baseline-запись: snapshot результата оценки перед изменением навыка.
///     Записывается в skills/.baselines/{skillId}.json.
/// </summary>
public sealed class BaselineRecord
{
    public required string Id { get; init; }
    public required string SkillId { get; init; }
    public required string SkillVersion { get; init; }
    public required double Score { get; init; }
    public required int TotalTests { get; init; }
    public required int PassedTests { get; init; }
    public required string RecordedAt { get; init; }
    public string? RecordedBy { get; init; } // "user", "agent", "system"
    public string? Reason { get; init; } // "pre-promotion", "manual", "pre-improvement"
    public List<TestCaseResult>? TestResults { get; init; }
}

/// <summary>
///     Результат одного тест-кейса в baseline.
/// </summary>
public sealed class TestCaseResult
{
    public required string Name { get; init; }
    public bool Passed { get; init; }
    public double Score { get; init; } // 0..1
    public string? FailureReason { get; init; }
}
