using Hercules.Agent;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Tools;

namespace Hercules.WebApi.Config;

/// <summary>
///     Фоновый сервис, который подписывается на изменения конфигурации
///     и инициирует перезагрузку runtime-зависимостей через <see cref="RuntimeConfigReactor" />.
/// </summary>
public sealed class RuntimeConfigHostedService : IHostedService
{
    private readonly RuntimeConfigReactor _reactor;

    public RuntimeConfigHostedService(
        RuntimeConfigStore store,
        LlmClientFactory factory,
        ResilientLLMClient resilient,
        RoleRouter roleRouter,
        Hercules.Tools.ToolRegistry tools,
        IEnumerable<IConfigReload> reloadConsumers)
    {
        _reactor = new RuntimeConfigReactor(store, factory, resilient, roleRouter, tools, reloadConsumers);
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
