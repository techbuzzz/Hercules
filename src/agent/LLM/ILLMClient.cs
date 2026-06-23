namespace Hercules.LLM;

/// <summary>
///     Имя роли по умолчанию для всех вызовов LLM (main = основной диалоговый клиент).
///     Другие роли: "code_writer", "reflector" — специализированные для задач v2.
/// </summary>
public static class Roles
{
    public const string Main = "main";
    public const string CodeWriter = "code_writer";
    public const string Reflector = "reflector";
}

/// <summary>
///     Единый интерфейс LLM-провайдера. Реализации оборачивают
///     Microsoft.Extensions.AI.IChatClient (OpenAI-совместимый транспорт).
/// </summary>
public interface ILLMClient
{
    /// <summary>Человекочитаемое имя провайдера (yandexgpt, ollama-cloud, ollama-local).</summary>
    string ProviderName { get; }

    /// <summary>Имя активной модели.</summary>
    string ModelName { get; }

    /// <summary>
    ///     Отправить набор сообщений через указанную роль (multi-role routing, v2).
    ///     Реализация обязана маршрутизировать на правильный провайдер/модель для роли.
    ///     Если роль не сконфигурирована — fallback на Roles.Main.
    /// </summary>
    Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);

    /// <summary>
    ///     Отправить набор сообщений и получить полный ответ.
    ///     Backward-compat overload — эквивалентно role=Roles.Main.
    /// </summary>
    Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);

    /// <summary>Потоковая генерация ответа для указанной роли (multi-role routing, v2).</summary>
    IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);

    /// <summary>Потоковая генерация ответа (по токенам/фрагментам). Backward-compat overload.</summary>
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);
}
