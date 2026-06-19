namespace Hercules.Storage;

/// <summary>
///     Метаданные навыка (skill.{id}.meta.json).
/// </summary>
public sealed class SkillMeta
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Triggers { get; set; } = new();
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public int Version { get; set; } = 1;
    public double SuccessRate { get; set; } = 1.0;
    public int TotalUses { get; set; } = 0;
}

/// <summary>
///     Полная модель навыка: метаданные + описание + system prompt.
/// </summary>
public sealed class Skill
{
    public SkillMeta Meta { get; set; } = new();

    /// <summary>Markdown-описание (что делает навык, когда вызывать).</summary>
    public string Description { get; set; } = "";

    /// <summary>System prompt навыка.</summary>
    public string Prompt { get; set; } = "";
}

/// <summary>Запись об одном использовании навыка (usage.json).</summary>
public sealed class SkillUsage
{
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Признак успеха использования (true/false).</summary>
    public bool Success { get; set; } = true;

    /// <summary>Уверенность ответа: high/medium/low.</summary>
    public string Confidence { get; set; } = "medium";
}

/// <summary>Запись взаимодействия для лога SQLite.</summary>
public sealed record InteractionLog(
    string SessionId,
    string Input,
    string Output,
    string Confidence,
    string Mode, // skill | direct
    string? SkillId,
    string Provider,
    DateTime CreatedAt);
