namespace Hercules.LLM;

/// <summary>Роль участника диалога.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant
}

/// <summary>Одно сообщение в диалоге с моделью.</summary>
/// <param name="Role">Роль автора сообщения.</param>
/// <param name="Content">Текстовое содержимое.</param>
public readonly record struct ChatTurn(ChatRole Role, string Content);

/// <summary>
///     Результат запроса к LLM.
/// </summary>
/// <param name="Text">Сгенерированный текст.</param>
/// <param name="Provider">Имя провайдера, который фактически ответил.</param>
/// <param name="Model">Имя использованной модели.</param>
/// <param name="InputTokens">Число токенов в запросе (input).</param>
/// <param name="OutputTokens">Число токенов в ответе (output).</param>
public sealed record LlmResponse(
    string Text,
    string Provider,
    string Model,
    int InputTokens = 0,
    int OutputTokens = 0);
