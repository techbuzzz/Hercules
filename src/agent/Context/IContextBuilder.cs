using Hercules.Storage;

namespace Hercules.Context;

/// <summary>
///     Interface for context assembly — выбирает релевантную память,
///     tool schemas и prior task state в рамках token-бюджета.
/// </summary>
public interface IContextBuilder
{
    /// <summary>
    ///     Собрать context block для текущего запроса.
    ///     Приоритизирует по importance, фильтрует по budget.
    /// </summary>
    /// <param name="input">Текущий user input (для relevance scoring).</param>
    /// <param name="sessionId">ID сессии.</param>
    /// <param name="skill">Активный навык (nullable).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>ContextAssembly с текстовым блоком и budget info.</returns>
    Task<ContextAssembly> BuildContextAsync(
        string input,
        string sessionId,
        Skill? skill,
        CancellationToken ct = default);

    /// <summary>
    ///     Сжать tool trace в episodic memory entry.
    ///     Если trace.Length >= CompressionThreshold — записывает compressed episode.
    /// </summary>
    /// <param name="trace">Список tool call entries.</param>
    /// <param name="sessionId">ID сессии.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True если записано, false если ниже порога.</returns>
    Task<bool> CompressTraceAsync(
        IReadOnlyList<ToolTraceEntry> trace,
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    ///     Оценить число токенов в тексте (простая эвристика: len/4).
    /// </summary>
    /// <param name="text">Текст для оценки.</param>
    /// <returns>Оценка числа токенов.</returns>
    int EstimateTokens(string text);

    /// <summary>
    ///     Получить текущий context budget info.
    /// </summary>
    ContextBudget GetCurrentBudget();
}
