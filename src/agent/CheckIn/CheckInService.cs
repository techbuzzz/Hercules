using Hercules.Audit;
using Microsoft.Extensions.Logging;

namespace Hercules.CheckIn;

/// <summary>Роль Studio-подключения (task_098, ADR-0005). Contribute — оператор, эксклюзивно
/// занимает агента. System — админ, параллельный мониторинг без модификации state.</summary>
public enum CheckInRole
{
    Contribute = 0,
    System = 1
}

/// <summary>
///     CheckIn/CheckOut протокол для Studio-подключений (task_098, ADR-0005).
///     Одновременно только один contribute-Studio может занимать агента.
///     System-Studio может подключаться параллельно для мониторинга (read-only,
///     не модифицирует state). TTL без heartbeat — 60s, после чего автоматический
///     checkout. Force-checkout — только system role, audit-loggable.
/// </summary>
public sealed class CheckInService : IDisposable
{
    public const int DefaultTtlSeconds = 60;
    public const int DefaultCleanupIntervalSeconds = 15;

    private readonly object _lock = new();
    private readonly ILogger<CheckInService> _logger;
    private readonly IAuditService? _audit;
    private readonly Timer? _cleanupTimer;
    private CheckInRecord? _active;
    private bool _disposed;

    public TimeSpan Ttl { get; }
    public TimeSpan CleanupInterval { get; }

    /// <summary>DI-конструктор: извлекает <see cref="IAuditService"/> через <see cref="IServiceProvider"/>.</summary>
    public CheckInService(ILogger<CheckInService> logger, IServiceProvider sp)
        : this(logger, sp.GetService(typeof(IAuditService)) as IAuditService, null, null)
    {
    }

    /// <summary>Конструктор для прямого использования (в т.ч. в unit-тестах).
    /// <paramref name="cleanupInterval"/> = <see cref="TimeSpan.Zero"/> отключает фоновый таймер.</summary>
    public CheckInService(
        ILogger<CheckInService> logger,
        IAuditService? audit = null,
        TimeSpan? ttl = null,
        TimeSpan? cleanupInterval = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audit = audit;
        Ttl = ttl ?? TimeSpan.FromSeconds(DefaultTtlSeconds);
        CleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(DefaultCleanupIntervalSeconds);
        if (CleanupInterval > TimeSpan.Zero)
        {
            _cleanupTimer = new Timer(_ => CleanupExpired(), null, CleanupInterval, CleanupInterval);
        }
    }

    /// <summary>Зарегистрировать Studio. Contribute role — эксклюзивно (один активный).
    /// System role — no-op, разрешён в параллель для мониторинга.</summary>
    public CheckInResult CheckIn(string studioId, string studioName, CheckInRole role)
    {
        if (string.IsNullOrWhiteSpace(studioId)) throw new ArgumentException("studioId обязателен", nameof(studioId));
        if (string.IsNullOrWhiteSpace(studioName)) throw new ArgumentException("studioName обязателен", nameof(studioName));

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CheckInService));

            // System role — параллельный мониторинг, state не меняется.
            if (role == CheckInRole.System)
            {
                _ = SafeAuditAsync(studioId, "checkin_monitor", studioName, "system role parallel monitor");
                return new CheckInResult(true, null, SnapshotLocked());
            }

            // Contribute role: проверяем, не занят ли агент другим contribute-Studio.
            if (_active is not null
                && _active.Role == CheckInRole.Contribute
                && !string.Equals(_active.StudioId, studioId, StringComparison.Ordinal))
            {
                return new CheckInResult(
                    false,
                    $"agent is checked out by {_active.StudioName}",
                    SnapshotLocked());
            }

            // Идемпотентная re-checkin (тот же studioId): обновляем heartbeat, имя.
            var now = DateTime.UtcNow;
            var wasFresh = _active is null;
            _active = new CheckInRecord(
                StudioId: studioId,
                StudioName: studioName,
                Role: role,
                CheckedInAt: _active?.CheckedInAt ?? now,
                LastHeartbeat: now);

            _ = SafeAuditAsync(
                studioId,
                wasFresh ? "checkin" : "checkin_reconnect",
                studioName,
                wasFresh ? null : "reconnect by same studioId");

            return new CheckInResult(true, null, SnapshotLocked());
        }
    }

    /// <summary>Обновить heartbeat. Возвращает true при успехе, false если активной сессии для studioId нет.</summary>
    public bool Heartbeat(string studioId, CheckInRole role)
    {
        if (string.IsNullOrWhiteSpace(studioId)) return false;

        lock (_lock)
        {
            if (_disposed) return false;

            if (role == CheckInRole.System)
            {
                // System не имеет записи, heartbeat для него — no-op success.
                return true;
            }

            if (_active is null) return false;
            if (!string.Equals(_active.StudioId, studioId, StringComparison.Ordinal)) return false;

            _active = _active with { LastHeartbeat = DateTime.UtcNow };
            return true;
        }
    }

    /// <summary>Освободить агента. Возвращает true при успехе.</summary>
    public bool CheckOut(string studioId, CheckInRole role)
    {
        if (string.IsNullOrWhiteSpace(studioId)) return false;

        lock (_lock)
        {
            if (_disposed) return false;

            if (role == CheckInRole.System) return true; // no-op

            if (_active is null) return false;
            if (!string.Equals(_active.StudioId, studioId, StringComparison.Ordinal)) return false;

            var leaving = _active;
            _active = null;
            _ = SafeAuditAsync(studioId, "checkout", leaving.StudioName, null);
            return true;
        }
    }

    /// <summary>Принудительное освобождение (только system role). Audit-loggable.</summary>
    public CheckInResult ForceCheckOut(string? by)
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CheckInService));

            if (_active is null)
            {
                return new CheckInResult(true, null, SnapshotLocked());
            }

            var kicked = _active;
            _active = null;
            _ = SafeAuditAsync(
                string.IsNullOrWhiteSpace(by) ? "system" : by,
                "force_checkout",
                kicked.StudioId,
                $"forced {kicked.StudioName}");
            return new CheckInResult(true, null, SnapshotLocked());
        }
    }

    /// <summary>Текущий статус агента (потокобезопасный snapshot).</summary>
    public CheckInStatus GetStatus()
    {
        lock (_lock)
        {
            return SnapshotLocked();
        }
    }

    /// <summary>Тестовая точка входа в cleanup-логику (запускается фоновым таймером в проде).</summary>
    internal void CleanupExpired()
    {
        CheckInRecord? expired = null;
        lock (_lock)
        {
            if (_active is null) return;
            if (_active.Role == CheckInRole.System) return;
            if (DateTime.UtcNow - _active.LastHeartbeat <= Ttl) return;

            expired = _active;
            _active = null;
        }

        if (expired is not null)
        {
            _logger.LogInformation(
                "CheckIn TTL истёк для {StudioId} ({StudioName}), автоматический checkout",
                expired.StudioId,
                expired.StudioName);
            _ = SafeAuditAsync("system", "auto_checkout_ttl_expired", expired.StudioId, expired.StudioName);
        }
    }

    private CheckInStatus SnapshotLocked()
    {
        if (_active is null)
        {
            return new CheckInStatus(false, null, null, (int)Ttl.TotalSeconds, null);
        }
        return new CheckInStatus(
            CheckedOut: true,
            CheckedOutBy: _active.StudioName,
            CheckedOutAt: _active.CheckedInAt,
            TtlSeconds: (int)Ttl.TotalSeconds,
            Role: _active.Role.ToString().ToLowerInvariant());
    }

    private async Task SafeAuditAsync(string actor, string action, string? target, string? details)
    {
        if (_audit is null) return;
        try
        {
            await _audit.LogAsync(actor, action, target, details).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Аудит не должен ломать основной flow.
            _logger.LogWarning(ex, "CheckInService: не удалось записать audit-событие {Action}", action);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer?.Dispose();
        }
    }
}

/// <summary>Внутренняя запись активного contribute-checkin.</summary>
public sealed record CheckInRecord(
    string StudioId,
    string StudioName,
    CheckInRole Role,
    DateTime CheckedInAt,
    DateTime LastHeartbeat);

/// <summary>Публичный snapshot состояния агента (отдаётся клиентам).
/// <see cref="CheckedOut"/> = true означает, что агент занят Studio.</summary>
public sealed record CheckInStatus(
    bool CheckedOut,
    string? CheckedOutBy,
    DateTime? CheckedOutAt,
    int TtlSeconds,
    string? Role);

/// <summary>Результат операции check-in/force-checkout.</summary>
public sealed record CheckInResult(
    bool Success,
    string? Error,
    CheckInStatus Status);
