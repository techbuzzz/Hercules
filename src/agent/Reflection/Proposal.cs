namespace Hercules.Reflection;

/// <summary>
///     Запись proposal для self-improvement.
///     Генерируется MaintenanceWorkflow, применяется через SelfImprovementService.
/// </summary>
public sealed class Proposal
{
    /// <summary>Уникальный ID proposal.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>ID навыка, для которого предложено улучшение.</summary>
    public string SkillId { get; set; } = "";

    /// <summary>Имя навыка (для UI).</summary>
    public string SkillName { get; set; } = "";

    /// <summary>Текущая версия навыка.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>Версия, к которой можно откатиться при проблемах.</summary>
    public int RollbackVersion { get; set; }

    /// <summary>Краткое резюме анализа (что было обнаружено).</summary>
    public string AnalysisSummary { get; set; } = "";

    /// <summary>Предлагаемый новый system prompt.</summary>
    public string ProposedPrompt { get; set; } = "";

    /// <summary>Предлагаемые новые phrase receivers (список).</summary>
    public List<string> ProposedPhrases { get; set; } = new();

    /// <summary>Фразы для удаления из текущего набора.</summary>
    public List<string> RemovedPhrases { get; set; } = new();

    /// <summary>Ожидаемый прирост success rate (0..1).</summary>
    public double ExpectedScoreGain { get; set; }

    /// <summary>Текущий success rate навыка.</summary>
    public double CurrentScore { get; set; }

    /// <summary>Прогнозируемый success rate после применения.</summary>
    public double PredictedScore { get; set; }

    /// <summary>Кто или что инициировало analysis (agent/user/system).</summary>
    public string TriggeredBy { get; set; } = "agent";

    /// <summary>Когда создан proposal.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда proposal был применён или отклонён.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Кем применён или отклонён.</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>Статус: Proposed | Approved | Applied | Rejected | Superseded.</summary>
    public ProposalStatus Status { get; set; } = ProposalStatus.Proposed;

    /// <summary>Результат eval harness после применения (JSON).</summary>
    public string? EvalResult { get; set; }

    /// <summary>Резюме отклонения, если есть.</summary>
    public string? RejectionReason { get; set; }
}

/// <summary>
///     Статусы proposal.
/// </summary>
public enum ProposalStatus
{
    Proposed,
    Approved,
    Applied,
    Rejected,
    Superseded
}

/// <summary>
///     Результат diff между текущей и предлагаемой версией навыка.
/// </summary>
public sealed class SkillDiff
{
    /// <summary>ID навыка.</summary>
    public string SkillId { get; set; } = "";

    /// <summary>Текущая версия.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>Следующая версия.</summary>
    public int NextVersion { get; set; }

    /// <summary>Новые фразы (PhraseReceivers).</summary>
    public List<string> AddedPhrases { get; set; } = new();

    /// <summary>Удалённые фразы.</summary>
    public List<string> RemovedPhrases { get; set; } = new();

    /// <summary>Без изменений.</summary>
    public List<string> UnchangedPhrases { get; set; } = new();

    /// <summary>Изменения в prompt (строки).</summary>
    public List<string> PromptDiffLines { get; set; } = new();

    /// <summary>Краткое описание изменений.</summary>
    public string DiffSummary { get; set; } = "";

    /// <summary>Версия для rollback.</summary>
    public int RollbackVersion { get; set; }
}
