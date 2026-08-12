namespace Hercules.Tools.Registry;

/// <summary>
///     Расширенный реестр инструментов с health tracking, allow/deny и категориями.
///     Заменяет базовый <see cref="ToolRegistry"/> в DI для компонентов, требующих
///     расширенной информации о tools.
/// </summary>
public interface IToolRegistryService
{
    /// <summary>Получить entry по имени.</summary>
    ToolRegistryEntry? GetEntry(string name);

    /// <summary>Все entries (включая disabled).</summary>
    IReadOnlyCollection<ToolRegistryEntry> GetAllEntries();

    /// <summary>Entries конкретной категории.</summary>
    IEnumerable<ToolRegistryEntry> GetByCategory(ToolCategory category);

    /// <summary>Только разрешённые (enabled и проходят allow/deny) tools.</summary>
    IEnumerable<string> GetAllowedTools();

    /// <summary>Проверить, разрешён ли tool.</summary>
    bool IsAllowed(string name);

    /// <summary>Обновить health state tool.</summary>
    void UpdateHealthState(string name, ToolHealthState state);

    /// <summary>Включить/выключить tool.</summary>
    void SetEnabled(string name, bool enabled);

    /// <summary>Перезагрузить registry (re-scan tool files).</summary>
    void Reload();

    /// <summary>Зарегистрировать tool из discovery source.</summary>
    void RegisterEntry(ToolRegistryEntry entry);
}
