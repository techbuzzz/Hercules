using System.Collections.Concurrent;

namespace Hercules.Mesh;

/// <summary>
///     Состояние circuit breaker для одного peer-агента.
/// </summary>
public enum CircuitState
{
    /// <summary>Цепь замкнута — запросы проходят.</summary>
    Closed,

    /// <summary>Цепь разомкнута — запросы отклоняются сразу, без обращения к peer'у.</summary>
    Open,

    /// <summary>Полу-открыта — пробный запрос для проверки восстановления peer'а.</summary>
    HalfOpen
}

/// <summary>
///     Запись о состоянии circuit breaker для одного peer-агента.
/// </summary>
internal sealed class CircuitStateRecord
{
    public CircuitState State { get; set; } = CircuitState.Closed;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset LastFailureAt { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>
///     Circuit Breaker для peer-вызовов в mesh.
///     Отслеживает неудачи каждого peer-агента и временно "размыкает цепь" —
///     отклоняет запросы без обращения к упавшему peer'у.
///     После cooldown-периода переходит в HalfOpen и пропускает один пробный запрос.
///     Спецификация: docs/ROADMAP-RU.md Phase 4 #19.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitStateRecord> _circuits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Порог неудач для размыкания цепи (по умолчанию 5).</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Cooldown-период перед переходом в HalfOpen (по умолчанию 60 секунд).</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Проверить, можно ли отправить запрос указанному peer'у.
    ///     Возвращает true, если цепь замкнута или полу-открыта (пробный запрос).
    ///     Возвращает false, если цепь разомкнута — запрос отклоняется без обращения к peer'у.
    /// </summary>
    public bool CanSend(string agentId)
    {
        if (!_circuits.TryGetValue(agentId, out var record))
        {
            return true; // Новый peer — цепь замкнута
        }

        lock (record)
        {
            switch (record.State)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open:
                    // Проверяем, не прошёл ли cooldown
                    if (DateTimeOffset.UtcNow - record.OpenedAt >= Cooldown)
                    {
                        record.State = CircuitState.HalfOpen;
                        return true; // Пробный запрос
                    }
                    return false; // Ещё в cooldown
                case CircuitState.HalfOpen:
                    return true; // Пробный запрос разрешён
                default:
                    return true;
            }
        }
    }

    /// <summary>Зафиксировать успешный вызов peer'а — сбрасывает счётчик неудач и замыкает цепь.</summary>
    public void RecordSuccess(string agentId)
    {
        if (_circuits.TryGetValue(agentId, out var record))
        {
            lock (record)
            {
                record.State = CircuitState.Closed;
                record.ConsecutiveFailures = 0;
            }
        }
    }

    /// <summary>
    ///     Зафиксировать неудачный вызов peer'а.
    ///     Если неудач подряд >= FailureThreshold — размыкает цепь на Cooldown-период.
    /// </summary>
    public void RecordFailure(string agentId)
    {
        var record = _circuits.GetOrAdd(agentId, _ => new CircuitStateRecord());
        lock (record)
        {
            record.ConsecutiveFailures++;
            record.LastFailureAt = DateTimeOffset.UtcNow;

            if (record.State == CircuitState.HalfOpen)
            {
                // Пробный запрос провалился — снова размыкаем
                record.State = CircuitState.Open;
                record.OpenedAt = DateTimeOffset.UtcNow;
            }
            else if (record.ConsecutiveFailures >= FailureThreshold)
            {
                record.State = CircuitState.Open;
                record.OpenedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>Получить текущее состояние цепи для peer'а (для диагностики и API).</summary>
    public CircuitState GetState(string agentId)
    {
        if (!_circuits.TryGetValue(agentId, out var record))
        {
            return CircuitState.Closed;
        }
        lock (record)
        {
            return record.State;
        }
    }

    /// <summary>Сбросить circuit breaker для peer'а (принудительно замкнуть цепь).</summary>
    public void Reset(string agentId)
    {
        if (_circuits.TryGetValue(agentId, out var record))
        {
            lock (record)
            {
                record.State = CircuitState.Closed;
                record.ConsecutiveFailures = 0;
            }
        }
    }

    /// <summary>Список всех отслеживаемых peer'ов и их состояний (для API и UI).</summary>
    public IReadOnlyDictionary<string, CircuitState> GetAllStates()
    {
        var result = new Dictionary<string, CircuitState>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _circuits)
        {
            lock (kvp.Value)
            {
                result[kvp.Key] = kvp.Value.State;
            }
        }
        return result;
    }
}

/// <summary>
///     Retry-политика с экспоненциальной задержкой для peer-вызовов.
///     Используется вместе с CircuitBreaker: retry выполняется только если цепь замкнута.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Максимум попыток (включая первую). По умолчанию 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Базовая задержка перед первой retry-попыткой. По умолчанию 500мс.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Множитель экспоненциальной задержки. По умолчанию 2.0.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Максимальная задержка между попытками. По умолчанию 5с.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Вычислить задержку перед N-й retry-попыткой (N = 1 для первой retry, 2 для второй и т.д.).
    /// </summary>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt <= 0) return TimeSpan.Zero;
        var delayMs = BaseDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt - 1);
        var clamped = Math.Min(delayMs, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(clamped);
    }

    /// <summary>
    ///     Определяет, стоит ли повторять запрос после ошибки.
    ///     Timeout и rejected — повторяем. HTTP 4xx — не повторяем (клиентская ошибка).
    ///     attempt = 0 для первой попытки, 1 для первой retry, и т.д.
    ///     Возвращает false если это была последняя попытка (attempt >= MaxAttempts - 1).
    /// </summary>
    public bool ShouldRetry(IntentResponse response, int attempt)
    {
        if (attempt >= MaxAttempts - 1) return false;
        return response.Status is "timeout" or "error";
    }
}