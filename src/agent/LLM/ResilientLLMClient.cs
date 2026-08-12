using System.Net;
using System.Runtime.CompilerServices;
using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.LLM;

/// <summary>
///     Отказоустойчивый LLM-клиент с поддержкой multi-role routing (v2).
///     Поддерживает fallback-цепочку для main-роли (как раньше)
///     и per-role маршрутизацию через RoleRouter.
/// </summary>
public sealed class ResilientLLMClient : ILLMClient
{
    private readonly ILLMClientFactory _factory;
    private readonly ILogger<ResilientLLMClient> _logger;
    private readonly RoleRouter _roleRouter;
    private volatile LlmConfig _cfg;
    private volatile List<(string Name, Lazy<ILLMClient> Client)> _mainChain;

    public ResilientLLMClient(LlmConfig cfg, ILLMClientFactory factory, RoleRouter roleRouter, ILogger<ResilientLLMClient> logger)
    {
        _cfg = cfg;
        _roleRouter = roleRouter;
        _factory = factory;
        _logger = logger;
        _mainChain = [];
        ProviderName = "";
        ModelName = "";
        RebuildChain(cfg);
    }

    /// <summary>Имя последнего успешно ответившего провайдера.</summary>
    public string ProviderName { get; private set; }

    public string ModelName { get; private set; }

    /// <summary>Retry constants.</summary>
    private const int MaxRetryAttempts = 3;
    private const int BaseDelayMs = 500;
    private const int MaxDelayMs = 8000;
    private const double JitterFraction = 0.25;

    /// <summary>HTTP status codes considered retryable.</summary>
    private static readonly HashSet<HttpStatusCode> RetryableCodes =
    [
        HttpStatusCode.TooManyRequests,       // 429
        HttpStatusCode.InternalServerError,   // 500
        HttpStatusCode.BadGateway,            // 502
        HttpStatusCode.ServiceUnavailable,    // 503
        HttpStatusCode.GatewayTimeout         // 504
    ];

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
    {
        return CompleteAsync(Roles.Main, messages, ct);
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
    {
        return StreamAsync(Roles.Main, messages, ct);
    }

    /// <summary>
    ///     Перезагрузить fallback-цепочку и активного провайдера по новой конфигурации.
    ///     Используется при runtime-изменении конфигурации через Web UI.
    /// </summary>
    public void Reload(LlmConfig cfg)
    {
        _cfg = cfg;
        RebuildChain(cfg);
    }

    private void RebuildChain(LlmConfig cfg)
    {
        var order = new List<string> { cfg.Provider };
        foreach (var fb in cfg.Fallback.Where(fb => !order.Contains(fb, StringComparer.OrdinalIgnoreCase)))
        {
            order.Add(fb);
        }

        var newChain = order
            .Select(name => (name, new Lazy<ILLMClient>(() => _factory.Create(name))))
            .ToList();

        _mainChain = newChain;

        ProviderName = cfg.Provider;
        ModelName = "";
    }

    private async Task<LlmResponse> CompleteMainAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct)
    {
        Exception? last = null;
        foreach ((var name, Lazy<ILLMClient> lazy) in _mainChain)
        {
            last = null;
            ILLMClient? client = null;
            for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                ILLMClient? usedClient = null;
                try
                {
                    // Access lazy.Value inside try — Lazy<T> factory throws propagate here
                    client = lazy.Value;
                    usedClient = client;
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
                    var canRetry = attempt < MaxRetryAttempts - 1 && IsRetryable(ex);
                    if (canRetry)
                    {
                        var delay = ComputeBackoff(attempt);
                        _logger.LogWarning(
                            "Provider '{Name}' attempt {Attempt}/{Max} failed ({Error}). Retrying in {Delay}ms...",
                            name, attempt + 1, MaxRetryAttempts, ex.Message, delay);
                        try
                        {
                            await Task.Delay(delay, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Provider '{Name}' unavailable: {Message}. Trying next...", name, ex.Message);
                        break;
                    }
                }
            }

            if (last is not null)
            {
                _logger.LogWarning("Provider '{Name}' exhausted retries. Trying next...", name);
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
            _logger.LogWarning("Role '{Role}' ({Provider}) unavailable: {Message}. Falling back to main.", role, client.ProviderName, ex.Message);
            return await CompleteMainAsync(messages, ct);
        }
    }

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
                _logger.LogWarning("Provider '{Name}' unavailable (stream): {Message}. Trying next...", name, ex.Message);
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
        await using IAsyncEnumerator<string> enumerator = client.StreamAsync(messages, ct).GetAsyncEnumerator(ct);
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

    /// <summary>Check if an exception is retryable (HTTP status or network).</summary>
    internal static bool IsRetryable(Exception ex)
    {
        // Http Pipeline: HttpRequestException or derived with status code
        if (ex is HttpRequestException hre && hre.StatusCode.HasValue)
        {
            return RetryableCodes.Contains(hre.StatusCode.Value);
        }

        // OpenAI SDK wraps HTTP status in inner exceptions
        var current = ex.InnerException;
        while (current is not null)
        {
            if (current is HttpRequestException ihre && ihre.StatusCode.HasValue)
            {
                return RetryableCodes.Contains(ihre.StatusCode.Value);
            }

            current = current.InnerException;
        }

        // Network-level errors are retryable
        return ex is not OperationCanceledException;
    }

    /// <summary>Compute exponential backoff with jitter.</summary>
    private static int ComputeBackoff(int attempt)
    {
        var exponential = BaseDelayMs * (int)Math.Pow(2, attempt);
        var capped = Math.Min(exponential, MaxDelayMs);
        var jitter = capped * JitterFraction;
        var rand = Random.Shared.NextDouble();
        var result = (int)(capped - jitter + 2 * jitter * rand);
        return Math.Max(100, result);
    }
}
