using System.Runtime.CompilerServices;
using Hercules.Config;

namespace Hercules.LLM;

/// <summary>
///     Отказоустойчивый LLM-клиент: пробует основной провайдер,
///     при ошибке последовательно переключается на fallback-провайдеры.
/// </summary>
public sealed class ResilientLLMClient : ILLMClient
{
    private readonly List<(string Name, Lazy<ILLMClient> Client)> _chain;

    public ResilientLLMClient(LlmConfig cfg, LlmClientFactory factory)
    {
        var order = new List<string> { cfg.Provider };
        foreach (var fb in cfg.Fallback.Where(fb => !order.Contains(fb, StringComparer.OrdinalIgnoreCase)))
        {
            order.Add(fb);
        }

        _chain = order
            .Select(name => (name, new Lazy<ILLMClient>(() => factory.Create(name))))
            .ToList();

        ProviderName = cfg.Provider;
        ModelName = "";
    }

    /// <summary>Имя последнего успешно ответившего провайдера.</summary>
    public string ProviderName { get; private set; }

    public string ModelName { get; private set; }

    public async Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach ((var name, Lazy<ILLMClient> lazy) in _chain)
        {
            try
            {
                ILLMClient client = lazy.Value;
                LlmResponse resp = await client.CompleteAsync(messages, ct);
                ProviderName = client.ProviderName;
                ModelName = client.ModelName;
                return resp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                await Console.Error.WriteLineAsync($"[LLM] Провайдер '{name}' недоступен: {ex.Message}. Пробую следующий...");
            }
        }

        throw new InvalidOperationException("Все LLM-провайдеры недоступны.", last);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Выбираем первый рабочий провайдер для стрима (с проверкой через быстрый старт).
        foreach ((var name, Lazy<ILLMClient> lazy) in _chain)
        {
            IAsyncEnumerator<string>? enumerator = null;
            var started = false;
            try
            {
                enumerator = lazy.Value.StreamAsync(messages, ct).GetAsyncEnumerator(ct);
                started = await enumerator.MoveNextAsync();
                ProviderName = lazy.Value.ProviderName;
                ModelName = lazy.Value.ModelName;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[LLM] Провайдер '{name}' недоступен (stream): {ex.Message}. Пробую следующий...");
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync();
                }

                continue;
            }

            // Успешно стартовали — отдаём поток до конца.
            try
            {
                if (started)
                {
                    yield return enumerator!.Current;
                    while (await enumerator.MoveNextAsync())
                    {
                        yield return enumerator.Current;
                    }
                }
            }
            finally
            {
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync();
                }
            }

            yield break;
        }

        throw new InvalidOperationException("Все LLM-провайдеры недоступны (stream).");
    }
}
