using Hercules.Storage;

namespace Hercules.Skills.Routing.Deterministic;

/// <summary>
///     Task 023: Детерминированный маршрутизатор навыков без embedding.
///     Использует keyword triggers, tags и declared input types.
///     Всегда доступен (offline-capable).
/// </summary>
public interface IDeterministicRouter
{
    /// <summary>
    ///     Маршрутизировать запрос, используя только keyword/tag/type matching.
    ///     Никогда не обращается к embedding-провайдеру.
    /// </summary>
    DeterministicRouteResult Route(string input);

    /// <summary>
    ///     Детерминированный роутер всегда доступен (offline-safe).
    /// </summary>
    bool IsAvailable { get; }
}
