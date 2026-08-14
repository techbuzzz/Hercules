using Hercules.Config;
using Hercules.LLM;
using Hercules.Tools;
using Microsoft.Extensions.Logging;

namespace Hercules.WebApi.Config;

/// <summary>
///     Фоновый сервис, который подписывается на изменения конфигурации
///     и инициирует перезагрузку runtime-зависимостей через <see cref="RuntimeConfigReactor" />.
/// </summary>
public sealed class RuntimeConfigHostedService(
    RuntimeConfigStore store,
    LlmClientFactory factory,
    ResilientLLMClient resilient,
    RoleRouter roleRouter,
    ToolRegistry tools,
    ILogger<RuntimeConfigReactor> logger,
    IEnumerable<IConfigReload> reloadConsumers)
    : IHostedService
{
    private readonly RuntimeConfigReactor _reactor = new(store, factory, resilient, roleRouter, tools, logger, reloadConsumers);

    public Task StartAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
