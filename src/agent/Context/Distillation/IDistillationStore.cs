namespace Hercules.Context.Distillation;

/// <summary>
///     Persistence для контекстной дистилляции (task_102).
///     Хранит summaries и key-facts по сессиям, чтобы они переживали restart агента.
/// </summary>
public interface IDistillationStore
{
    /// <summary>Сохранить summary для сессии. Возвращает id созданной записи.</summary>
    Task<long> SaveSummaryAsync(DistillationSummary summary, CancellationToken ct = default);

    /// <summary>Получить все summaries сессии, упорядоченные по <see cref="DistillationSummary.FromIndex"/>.</summary>
    Task<IReadOnlyList<DistillationSummary>> GetSummariesAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Удалить все summaries сессии (используется при reset сессии).</summary>
    Task DeleteSummariesAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Сохранить key-facts (upsert по тексту факта в рамках сессии).</summary>
    Task SaveKeyFactsAsync(string sessionId, IReadOnlyList<KeyFact> facts, CancellationToken ct = default);

    /// <summary>Получить top-N key-facts сессии, отсортированных по score DESC.</summary>
    Task<IReadOnlyList<KeyFact>> GetKeyFactsAsync(string sessionId, int limit, CancellationToken ct = default);

    /// <summary>Удалить все key-facts сессии.</summary>
    Task DeleteKeyFactsAsync(string sessionId, CancellationToken ct = default);
}
