namespace Hercules.Context.Distillation;

/// <summary>
///     Уровень хранения в иерархии дистилляции (task_102).
///     <list type="bullet">
///         <item><description><c>Recent</c> — последние N сообщений, raw.</description></item>
///         <item><description><c>Summary</c> — older messages, сжатые в summary.</description></item>
///         <item><description><c>Ancient</c> — самые старые, key-facts.</description></item>
///     </list>
/// </summary>
public enum DistillationTier
{
    Recent,
    Summary,
    Ancient
}

/// <summary>
///     Один key-fact, извлечённый из ancient сообщений (task_102).
/// </summary>
/// <param name="FactText">Краткий текст факта.</param>
/// <param name="Score">Score факта (frequency * importance).</param>
/// <param name="SourceCount">Сколько сообщений содержали этот факт.</param>
/// <param name="FirstSeen">UTC timestamp первого появления.</param>
public sealed record KeyFact(
    string FactText,
    double Score,
    int SourceCount,
    DateTime FirstSeen);

/// <summary>
///     Summary, сгенерированный для сессии (task_102).
///     Хранится в <c>context_summaries</c>.
/// </summary>
/// <param name="Id">DB id (auto-increment).</param>
/// <param name="SessionId">ID сессии.</param>
/// <param name="FromIndex">Начало диапазона сообщений (включительно).</param>
/// <param name="ToIndex">Конец диапазона сообщений (включительно).</param>
/// <param name="Summary">Текст сводки (markdown).</param>
/// <param name="MessageCount">Сколько сообщений вошло в сводку.</param>
/// <param name="TokenEstimate">Оценка числа токенов в сводке.</param>
/// <param name="CreatedAt">UTC timestamp создания.</param>
public sealed record DistillationSummary(
    long Id,
    string SessionId,
    int FromIndex,
    int ToIndex,
    string Summary,
    int MessageCount,
    int TokenEstimate,
    DateTime CreatedAt);

/// <summary>
///     Результат одного запуска дистилляции (task_102).
///     Возвращается из <c>POST /api/context/distill</c>.
/// </summary>
/// <param name="SessionId">ID сессии.</param>
/// <param name="RecentCount">Сколько raw-сообщений оставлено в recent tier.</param>
/// <param name="SummariesCreated">Сколько новых summary создано в этом запуске.</param>
/// <param name="KeyFactsExtracted">Сколько key-facts извлечено.</param>
/// <param name="TokensBefore">Общая оценка токенов до дистилляции.</param>
/// <param name="TokensAfter">Общая оценка токенов после (recent raw + новые summaries + key facts).</param>
/// <param name="SummaryMarkdown">Сводный markdown-блок всего дистиллированного контекста.</param>
public sealed record DistillationResult(
    string SessionId,
    int RecentCount,
    int SummariesCreated,
    int KeyFactsExtracted,
    int TokensBefore,
    int TokensAfter,
    string SummaryMarkdown);

/// <summary>
///     Промежуточное представление сообщения для дистилляции (task_102).
///     Нормализует разные источники (interactions table, in-memory) к единому виду.
/// </summary>
/// <param name="Index">Порядковый индекс в сессии (0-based).</param>
/// <param name="SessionId">ID сессии.</param>
/// <param name="Input">Текст user/agent input.</param>
/// <param name="Output">Текст ответа.</param>
/// <param name="Confidence">"high"/"medium"/"low".</param>
/// <param name="CreatedAt">UTC timestamp.</param>
public sealed record ConversationMessage(
    int Index,
    string SessionId,
    string Input,
    string Output,
    string Confidence,
    DateTime CreatedAt);
