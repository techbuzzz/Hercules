using System.Text.Json;
using Hercules.Audit;
using Microsoft.Extensions.Logging;

namespace Hercules.Restart;

/// <summary>
///     Supervisor-протокол для удалённого перезапуска агента (task_099).
///     <para>
///         Флаг <c>restart-pending</c> персистится в <c>{DataRoot}/restart-state.json</c>,
///         чтобы пережить рестарт процесса. Агент <b>не убивает себя сам</b> — внешний
///         supervisor (Studio / systemd / Windows Service / watcher) опрашивает
///         <c>GET /api/system/restart-pending</c> и сам выполняет kill. После рестарта
///         агент auto-clear'ит устаревший флаг, т.к. предыдущий restart уже выполнен.
///     </para>
///     <para>
///         Помещён в namespace <c>Hercules.Restart</c> вместо <c>Hercules.System</c>,
///         чтобы не shadow'ить BCL (<c>System.*</c>) — тот же урок, что и в task_098
///         (CheckIn вынесен из <c>System/</c> в <c>CheckIn/</c>).
///     </para>
/// </summary>
public sealed class RestartService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly ILogger<RestartService> _logger;
    private readonly IAuditService? _audit;
    private readonly string _stateFilePath;
    private RestartState _state;

    /// <summary>DI-конструктор: <see cref="IAuditService"/> resolve'ится через <see cref="IServiceProvider"/>.</summary>
    public RestartService(ILogger<RestartService> logger, IServiceProvider sp)
        : this(logger, sp.GetService(typeof(IAuditService)) as IAuditService, DefaultStatePath())
    {
    }

    /// <summary>Конструктор для прямого использования (в т.ч. unit-тестах).</summary>
    /// <param name="stateFilePath">Путь к JSON-файлу с состоянием restart-флага.</param>
    public RestartService(ILogger<RestartService> logger, IAuditService? audit, string stateFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audit = audit;
        _stateFilePath = stateFilePath ?? throw new ArgumentNullException(nameof(stateFilePath));

        var dir = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Загружаем сохранённое состояние. Если файл отсутствует — стартуем чисто.
        // Если pending — auto-clear: предыдущий restart либо уже выполнен supervisor'ом,
        // либо stale (например, agent был убит до restart). В обоих случаях новый процесс
        // не должен сразу же рестартоваться.
        _state = LoadFromDisk();
        if (_state.Pending)
        {
            _logger.LogInformation(
                "RestartService: при старте обнаружен pending-флаг (запрошен {RequestedAt} пользователем {RequestedBy}, reason: {Reason}); auto-clear — предыдущий restart уже завершён",
                _state.RequestedAt,
                _state.RequestedBy,
                _state.Reason);
            _state = new RestartState(false, null, null, null);
            SaveToDisk();
        }
    }

    /// <summary>Запросить рестарт. Возвращает snapshot состояния после установки флага.</summary>
    /// <param name="reason">Причина рестарта (operator-supplied).</param>
    /// <param name="requestedBy">Actor для audit-лога (e.g. "studio", "operator").</param>
    public RestartState RequestRestart(string? reason, string? requestedBy)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _state = new RestartState(
                Pending: true,
                RequestedAt: now,
                Reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                RequestedBy: string.IsNullOrWhiteSpace(requestedBy) ? "system" : requestedBy.Trim());
            SaveToDisk();
        }

        _logger.LogWarning(
            "RestartService: запрошен рестарт агента (actor={Actor}, reason={Reason}, at={At:o})",
            _state.RequestedBy,
            _state.Reason,
            _state.RequestedAt);
        _ = SafeAuditAsync(_state.RequestedBy!, "restart_requested", null, _state.Reason);
        return _state;
    }

    /// <summary>Текущее состояние restart-флага (snapshot, потокобезопасно).</summary>
    public RestartState GetStatus()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    /// <summary>Удобный accessor для polling'а supervisor'ом.</summary>
    public bool IsRestartPending()
    {
        lock (_lock)
        {
            return _state.Pending;
        }
    }

    /// <summary>Очистить restart-флаг. Возвращает true если флаг был установлен, false если уже чисто.</summary>
    /// <param name="clearedBy">Actor для audit-лога (e.g. supervisor, agent startup).</param>
    public bool ClearRestartRequest(string? clearedBy = null)
    {
        bool wasPending;
        lock (_lock)
        {
            wasPending = _state.Pending;
            if (!wasPending) return false;
            _state = new RestartState(false, null, null, null);
            SaveToDisk();
        }

        var actor = string.IsNullOrWhiteSpace(clearedBy) ? "system" : clearedBy.Trim();
        _logger.LogInformation("RestartService: restart-флаг очищен actor={Actor}", actor);
        _ = SafeAuditAsync(actor, "restart_cleared", null, null);
        return true;
    }

    private RestartState LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new RestartState(false, null, null, null);
            }
            var json = File.ReadAllText(_stateFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RestartState(false, null, null, null);
            }
            var loaded = JsonSerializer.Deserialize<RestartState>(json, JsonOptions);
            return loaded ?? new RestartState(false, null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RestartService: не удалось прочитать {File}, стартуем с чистого состояния",
                _stateFilePath);
            return new RestartState(false, null, null, null);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            var tmp = _stateFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _stateFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RestartService: не удалось сохранить restart-state в {File}",
                _stateFilePath);
        }
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
            _logger.LogWarning(ex, "RestartService: не удалось записать audit-событие {Action}", action);
        }
    }

    private static string DefaultStatePath()
    {
        // Фолбэк для DI-конструктора, если stateFilePath не указан явно в Program.cs.
        // Production-overwrite делается в Program.cs через DI-фабрику.
        return Hercules.BuiltIn.DataPath(Hercules.BuiltIn.RestartStateFileName);
    }
}

/// <summary>Snapshot состояния restart-флага. Передаётся в HTTP-ответах и audit-логе.</summary>
/// <param name="Pending">true если рестарт запрошен и supervisor должен выполнить kill.</param>
/// <param name="RequestedAt">UTC timestamp момента <see cref="RestartService.RequestRestart"/>.</param>
/// <param name="Reason">Operator-supplied причина (опционально).</param>
/// <param name="RequestedBy">Actor, запросивший рестарт (operator / system / studio).</param>
public sealed record RestartState(
    bool Pending,
    DateTime? RequestedAt,
    string? Reason,
    string? RequestedBy);
