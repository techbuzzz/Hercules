namespace Hercules.Mesh.Verification;

/// <summary>
///     High-impact или safety-sensitive ответы проверяются verifier-навыками,
///     числовыми валидаторами, policy-enforcer'ами или независимыми peer-агентами
///     до возврата пользователю или выполнения действия.
///
///     Спецификация: docs/ROADMAP-RU.md Phase 4 task_046.
/// </summary>

/// <summary>
///     Контекст одного запуска верификации. Создаётся на основе ответа агента
///     и передаётся каждому verifier'у.
/// </summary>
public sealed record VerificationContext
{
    /// <summary>Уникальный ID верификации (ULID).</summary>
    public string VerificationId { get; init; } = "";

    /// <summary>RequestId оригинального запроса.</summary>
    public string RequestId { get; init; } = "";

    /// <summary>ID агента-отправителя.</summary>
    public string AgentId { get; init; } = "";

    /// <summary>ID сессии.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Текст ответа, подлежащий верификации.</summary>
    public string ResponseText { get; init; } = "";

    /// <summary>Режим ответа: "skill" | "direct" | "tool".</summary>
    public string Mode { get; init; } = "direct";

    /// <summary>Имя tool'а, если mode=tool.</summary>
    public string? ToolUsed { get; init; }

    /// <summary>Confidence от агента: "high" | "medium" | "low".</summary>
    public string Confidence { get; init; } = "medium";

    /// <summary>Имя LLM-провайдера.</summary>
    public string Provider { get; init; } = "";

    /// <summary>Если true — верификация запущена в dry-run режиме.</summary>
    public bool IsDryRun { get; init; }

    /// <summary>UTC timestamp начала верификации.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
