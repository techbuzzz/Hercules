namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     Состояния delegated задачи в inter-agent протоколе.
///     Начальное состояние после AcceptAsync — Accepted.
/// </summary>
public enum DelegatedTaskState
{
    /// <summary>Задача принята к исполнению, execution ещё не начат.</summary>
    Accepted,

    /// <summary>Активно исполняется.</summary>
    Working,

    /// <summary>Исполнение приостановлено, ожидается ввод от вызывающего агента.</summary>
    AwaitingInput,

    /// <summary>Успешно завершена с результатом.</summary>
    Completed,

    /// <summary>Завершена с ошибкой.</summary>
    Failed,

    /// <summary>Отменена вызывающим агентом.</summary>
    Cancelled,

    /// <summary>Превышен deadline / задача истекла.</summary>
    Expired
}
