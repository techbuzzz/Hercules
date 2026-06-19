namespace Hercules.LLM;

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

    /// <summary>Отправить набор сообщений и получить полный ответ.</summary>
    Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);

    /// <summary>Потоковая генерация ответа (по токенам/фрагментам).</summary>
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default);
}
