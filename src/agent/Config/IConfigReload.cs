namespace Hercules.Config;

/// <summary>
///     Контракт для сервисов, которые могут применять изменения <see cref="AppConfig" />
///     во время работы приложения без перезагрузки (hot-reload).
///     Реализуется сервисами, чьи настройки можно обновить заменой полей.
///     Сервисы, требующие пересоздания ресурсов (SQLite-подключение, файловые дескрипторы,
///     sandbox-процессы), этот интерфейс НЕ реализуют — для них смена конфигурации
///     требует перезапуска приложения.
/// </summary>
public interface IConfigReload
{
    /// <summary>
    ///     Применить новую конфигурацию. Вызывается <see cref="RuntimeConfigReactor" />
    ///     при изменении <see cref="RuntimeConfigStore" />.
    /// </summary>
    void Reload(AppConfig config);
}
