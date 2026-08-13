namespace Hercules.Mesh.A2A;

/// <summary>
///     Сервис для работы с A2A Agent Card.
///     Публикует локальный Agent Card и импортирует remote Agent Cards для discovery.
/// </summary>
public interface IAgentCardService
{
    /// <summary>
    ///     Получить текущий A2A Agent Card, сгенерированный из локального AgentManifest.
    /// </summary>
    Task<AgentCard> GetAgentCardAsync(CancellationToken ct = default);

    /// <summary>
    ///     Импортировать Agent Card с remote URL (HTTP GET).
    /// </summary>
    /// <param name="url">URL Agent Card (e.g. "https://peer.example.com/agent-card.json")</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>Импортированный AgentCard или исключение</returns>
    Task<AgentCard> ImportFromUrlAsync(string url, CancellationToken ct = default);

    /// <summary>
    ///     Проверить, актуален ли локальный кэш Agent Card (freshness check).
    ///     Возвращает true, если кэш ещё не устарел (CacheTtlMinutes не прошёл).
    /// </summary>
    bool IsCurrent();

    /// <summary>
    ///     Опубликовать локальный Agent Card в файл agent-card.json.
    /// </summary>
    /// <param name="ct">CancellationToken</param>
    /// <returns>Путь к файлу</returns>
    Task<string> PublishAsync(CancellationToken ct = default);

    /// <summary>
    ///     Загрузить импортированные Agent Cards из remote discovery endpoints.
    /// </summary>
    /// <param name="urls">Список URLs для fetch</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>Список успешно импортированных Agent Cards</returns>
    Task<List<(string Url, AgentCard Card)>> DiscoverAsync(IEnumerable<string> urls, CancellationToken ct = default);
}
