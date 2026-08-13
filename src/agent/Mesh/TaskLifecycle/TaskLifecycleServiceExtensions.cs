using Hercules.Mesh.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Hercules.Mesh.TaskLifecycle;

/// <summary>
///     DI-регистрация TaskLifecycleProtocol (Phase 3, task_036).
/// </summary>
public static class TaskLifecycleServiceExtensions
{
    /// <summary>
    ///     Зарегистрировать <see cref="ITaskLifecycleProtocol"/> в DI.
    ///     Использует <paramref name="localAgentId"/> для callback source identification.
    /// </summary>
    public static IServiceCollection AddTaskLifecycleProtocol(
        this IServiceCollection services,
        string localAgentId)
    {
        // Register with ITransport for callbacks
        services.AddSingleton<ITaskLifecycleProtocol>(sp =>
        {
            var transport = sp.GetService<ITransport>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TaskLifecycleProtocol>>();
            return new TaskLifecycleProtocol(transport, localAgentId, logger);
        });

        return services;
    }
}
