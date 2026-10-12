namespace Hercules.Mesh.Abstractions;

/// <summary>
///     Shared state store для cross-agent workflow state в mesh.
///     Поддерживает: key-value, optimistic locking (ETag/Version), TTL, compare-and-set, watch.
///     Реализации: <see cref="InProcess.InProcessMeshStateStore"/> (default),
///     Redis/Valkey (task_067), PostgreSQL (task_069).
///     Спецификация: task_066.
/// </summary>
public interface IMeshStateStore : IDisposable
{
    /// <summary>
    ///     Backend kind: "in-process" | "redis" | "postgres".
    /// </summary>
    string BackendKind { get; }

    /// <summary>
    ///     Получить значение по ключу.
    /// </summary>
    /// <param name="key">Ключ (например, "workflow/{workflowId}/state").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Значение или null если не найдено.</returns>
    Task<StoredValue?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Установить значение. Перезаписывает существующее без проверки.
    ///     Для conditional update используйте <see cref="CompareAndSetAsync"/>.
    /// </summary>
    /// <param name="key">Ключ.</param>
    /// <param name="value">Значение (сериализуется в JSON).</param>
    /// <param name="ttl">TTL — null означает permanent.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAsync(string key, StoredValue value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    ///     Atomic compare-and-set: устанавливает значение только если текущий ETag/Version совпадает.
    ///     Возвращает true при успехе, false если key отсутствует или version mismatch.
    ///     Для optimistic locking между агентами.
    /// </summary>
    /// <param name="key">Ключ.</param>
    /// <param name="value">Новое значение.</param>
    /// <param name="expectedVersion">Ожидаемая версия (ETag). Null = создать если нет, отказать если есть.</param>
    /// <param name="ttl">TTL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True если успешно обновлено; false если version mismatch или key не существует.</returns>
    Task<bool> CompareAndSetAsync(
        string key,
        StoredValue value,
        string? expectedVersion,
        TimeSpan? ttl = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Удалить ключ.
    /// </summary>
    /// <param name="key">Ключ.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Проверить существование ключа.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     INCR / DECR числового значения. Создаёт ключ с 0 если отсутствует.
    ///     Опциональный <paramref name="ttl"/> применяется при создании ключа (или принудительно
    ///     обновляется на каждом increment, см. реализацию).
    /// </summary>
    /// <param name="key">Ключ.</param>
    /// <param name="delta">Дельта (+1 / -1).</param>
    /// <param name="ttl">TTL — null означает permanent. Поддерживается всеми backends.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    ///     Подписаться на изменения ключа. Callback вызывается при каждом изменении.
    ///     Возвращает disposable для отписки.
    /// </summary>
    /// <param name="key">Ключ (поддерживает wildcard "workflow/*/state" если backend позволяет).</param>
    /// <param name="handler">Callback при изменении. Получает новую версию.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IDisposable> WatchAsync(
        string key,
        Func<StoredValue, CancellationToken, Task> handler,
        CancellationToken ct = default);

    /// <summary>
    ///     Scan keys по prefix. Используется для enumerate workflow instances, sessions, etc.
    ///     Поддерживается ли — зависит от backend.
    /// </summary>
    /// <param name="prefix">Prefix ключа.</param>
    /// <param name="limit">Max возвращаемых записей.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<string>> ScanKeysAsync(
        string prefix,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    ///     Проверить connectivity до state backend.
    /// </summary>
    ValueTask<bool> IsHealthyAsync(CancellationToken ct = default);
}

/// <summary>
///     Value с metadata, хранимое в <see cref="IMeshStateStore"/>.
/// </summary>
public sealed record StoredValue
{
    /// <summary>Сериализованное значение (JSON).</summary>
    public required string Data { get; init; }

    /// <summary>Версия / ETag. Присваивается при каждом изменении.</summary>
    public required string Version { get; init; }

    /// <summary>Время создания записи.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Время последнего изменения.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>TTL expiry (если задан).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>AgentId последнего записавшего агента.</summary>
    public string? LastWriterAgentId { get; set; }
}
