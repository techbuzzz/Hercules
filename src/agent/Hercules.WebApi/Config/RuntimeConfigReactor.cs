using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Tools;

namespace Hercules.WebApi.Config;

/// <summary>
///     Слушатель изменений <see cref="RuntimeConfigStore" />.
///     Пересоздаёт LLM-клиенты и другие runtime-зависимости, которые нельзя
///     обновить простой заменой полей в конфигурации.
/// </summary>
public sealed class RuntimeConfigReactor
{
    private readonly RuntimeConfigStore _store;
    private readonly LlmClientFactory _factory;
    private readonly ResilientLLMClient _resilient;
    private readonly RoleRouter _roleRouter;
    private readonly ToolRegistry _tools;
    private readonly IEnumerable<IConfigReload> _reloadConsumers;

    public RuntimeConfigReactor(
        RuntimeConfigStore store,
        LlmClientFactory factory,
        ResilientLLMClient resilient,
        RoleRouter roleRouter,
        ToolRegistry tools,
        IEnumerable<IConfigReload> reloadConsumers)
    {
        _store = store;
        _factory = factory;
        _resilient = resilient;
        _roleRouter = roleRouter;
        _tools = tools;
        _reloadConsumers = reloadConsumers;
        _store.Changed += OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, AppConfig cfg)
    {
        // LLM-зависимости не кэшируют провайдеры на уровне конфигурации — фабрика
        // создаёт клиентов по запросу. Однако ResilientLLMClient хранит fallback-цепочку
        // и имя текущего провайдера, а RoleRouter кэширует клиентов по ролям.
        // Сбрасываем внутреннее состояние, чтобы новые вызовы использовали свежие настройки.
        _resilient.Reload(cfg.Llm);
        _roleRouter.Reload(cfg.Roles, cfg.Llm.Provider);
        _tools.Reload(cfg);

        // Прокидываем новую конфигурацию во все сервисы, поддерживающие hot-reload
        // (AgentCore, SkillManager, и другие IConfigReload-реализации).
        foreach (var consumer in _reloadConsumers)
        {
            try
            {
                consumer.Reload(cfg);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RuntimeConfigReactor] {consumer.GetType().Name}.Reload failed: {ex.Message}");
            }
        }

        Console.Error.WriteLine("[RuntimeConfigReactor] Конфигурация применена без перезагрузки.");
    }
}
