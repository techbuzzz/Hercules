using System.Runtime.CompilerServices;
using Hercules.Config;

namespace Hercules.LLM;

/// <summary>
///     Отказоустойчивый LLM-клиент с поддержкой multi-role routing (v2).
///     Поддерживает fallback-цепочку для main-роли (как раньше)
///     и per-role маршрутизацию через RoleRouter.
/// </summary>
public sealed class ResilientLLMClient : ILLMClient
{
    private readonly List<(string Name, Lazy<ILLMClient> Client)> _mainChain;
    private readonly RoleRouter _roleRouter;
    private readonly LlmConfig _cfg;

    public ResilientLLMClient(LlmConfig cfg, LlmClientFactory factory, RoleRouter roleRouter)
    {
        _cfg = cfg;
        _roleRouter = roleRouter;
        var order = new List<string> { cfg.Provider };
        foreach (var fb in cfg.Fallback.Where(fb => !order.Contains(fb, StringComparer.OrdinalIgnoreCase)))
        {
            order.Add(fb);
        }

        _mainChain = order
            .Select(name => (name, new Lazy<ILLMClient>(() => factory.Create(name))))
            .ToList();

        ProviderName = cfg.Provider;
        ModelName = "";
    }

    /// <summary>Имя последнего успешно ответившего провайдера.</summary>
    public string ProviderName { get; private set; }

    public string ModelName { get; private set; }

    public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        // main → fallback chain (как раньше)
        if (string.IsNullOrEmpty(role) || role == Roles.Main)
        {
            return CompleteMainAsync(messages, ct);
        }

        // Другая роль → RoleRouter → конкретный клиент (single-shot, без fallback).
        // Если роль не сконфигурирована — fallback на main.
        ILLMClient client = _roleRouter.Resolve(role);
        return InvokeRoleAsync(client, role, messages, ct);
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => CompleteAsync(Roles.Main, messages, ct);

    private async Task<LlmResponse> CompleteMainAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct)
    {
        Exception? last = null;
        foreach ((var name, Lazy<ILLMClient> lazy) in _mainChain)
        {
            try
            {
                LlmResponse resp = await lazy.Value.CompleteAsync(messages, ct);
                ProviderName = lazy.Value.ProviderName;
                ModelName = lazy.Value.ModelName;
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

    private async Task<LlmResponse> InvokeRoleAsync(
        ILLMClient client, string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct)
    {
        try
        {
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
            // Fallback: если роль-клиент упал — пробуем main-цепочку
            await Console.Error.WriteLineAsync(
                $"[LLM] Роль '{role}' ({client.ProviderName}) недоступна: {ex.Message}. Fallback на main.");
            return await CompleteMainAsync(messages, ct);
        }
    }

    public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(role) || role == Roles.Main)
        {
            return StreamMainAsync(messages, ct);
        }

        ILLMClient client = _roleRouter.Resolve(role);
        return StreamSingleAsync(client, role, messages, ct);
    }

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        => StreamAsync(Roles.Main, messages, ct);

    private async IAsyncEnumerable<string> StreamMainAsync(
        IReadOnlyList<ChatTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach ((var name, Lazy<ILLMClient> lazy) in _mainChain)
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

    private async IAsyncEnumerable<string> StreamSingleAsync(
        ILLMClient client,
        string role,
        IReadOnlyList<ChatTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var enumerator = client.StreamAsync(messages, ct).GetAsyncEnumerator(ct);
        var started = await enumerator.MoveNextAsync();
        ProviderName = client.ProviderName;
        ModelName = client.ModelName;
        if (started)
        {
            yield return enumerator.Current;
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
    }
}
