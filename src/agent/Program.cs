using System.Text;
using Hercules.Agent;
using Hercules.CLI;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Storage;
using Hercules.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ============================================================================
//  Hercules — самообучающийся микроагент (C# 15 / .NET 10)
//  Точка входа: настройка конфигурации, DI и запуск выбранного интерфейса.
// ============================================================================

// Поддержка корректного отображения кириллицы
Console.OutputEncoding = Encoding.UTF8;

// --- 1. Конфигурация ---
IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddUserSecrets<Program>()
    .AddJsonFile("appsettings.json", false, false)
    .AddEnvironmentVariables("HERCULES_")
    .Build();

AppConfig appConfig = configuration.Get<AppConfig>() ?? new AppConfig();

// --- 2. Dependency Injection ---
var services = new ServiceCollection();

// Конфигурационные секции
services.AddSingleton(appConfig);
services.AddSingleton(appConfig.Llm);
services.AddSingleton(appConfig.Storage);
services.AddSingleton(appConfig.Agent);
services.AddSingleton(appConfig.Telegram);
services.AddSingleton(appConfig.CodeExecution);
services.AddSingleton(appConfig.Http);
services.AddSingleton(appConfig.Mcp);
services.AddSingleton(appConfig.A2A);

// LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
services.AddSingleton<LlmClientFactory>();
services.AddSingleton<RoleRouter>();
services.AddSingleton<ILLMClient>(sp =>
    new ResilientLLMClient(
        sp.GetRequiredService<LlmConfig>(),
        sp.GetRequiredService<LlmClientFactory>(),
        sp.GetRequiredService<RoleRouter>()));

// Code execution (Stage 2, v2)
services.AddSingleton<Hercules.CodeExecution.SandboxOptions>(sp =>
{
    var cfg = sp.GetRequiredService<CodeExecutionConfig>();
    var opts = new Hercules.CodeExecution.SandboxOptions
    {
        CpuTimeoutSeconds = cfg.CpuTimeoutSeconds,
        MaxFileSizeMb = cfg.MaxFileSizeMb,
        MaxProcesses = cfg.MaxProcesses,
        MaxOpenFiles = cfg.MaxOpenFiles,
        MaxVirtualMemoryMb = cfg.MaxVirtualMemoryMb,
        AllowNetwork = cfg.AllowNetwork,
        MaxCodeSizeKb = cfg.MaxCodeSizeKb,
        SessionTtlSeconds = cfg.SessionTtlSeconds,
    };
    if (!string.IsNullOrWhiteSpace(cfg.TempRoot))
    {
        opts.TempRoot = cfg.TempRoot;
    }
    return opts;
});
services.AddSingleton<Hercules.CodeExecution.ICodeExecutor, Hercules.CodeExecution.DotnetFileBasedExecutor>();

// Tool ecosystem (Stage 3, v2)
services.AddSingleton<Hercules.Tools.ITool, Hercules.Tools.HttpTool>();
services.AddSingleton<Hercules.Tools.ITool, Hercules.Tools.A2AClient>();
services.AddSingleton<Hercules.Tools.ITool, Hercules.Tools.CodeExecutionTool>();
services.AddSingleton<Hercules.Tools.ToolRegistry>();
services.AddSingleton<Hercules.Tools.McpClient>();

// Хранилища
services.AddSingleton<FileSkillRepository>();
services.AddSingleton<MemoryStore>();
services.AddSingleton<SqliteSessionStore>();

// Агент
services.AddSingleton<SkillManager>();
services.AddSingleton<SkillRouter>();
services.AddSingleton<MemoryManager>();
services.AddSingleton<ReflectionEngine>();
services.AddSingleton<AgentCore>();

// Интерфейсы
services.AddSingleton<ConsoleUI>();
services.AddSingleton<TelegramBotInterface>();

await using ServiceProvider provider = services.BuildServiceProvider();

// --- 3. Выбор режима запуска ---
// Аргументы: --telegram запускает Telegram-бот, иначе — CLI (по умолчанию).
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var telegramMode = args.Contains("--telegram") || (appConfig.Telegram.Enabled && args.Contains("--bot"));

try
{
    if (telegramMode)
    {
        TelegramBotInterface bot = provider.GetRequiredService<TelegramBotInterface>();
        Console.WriteLine("Запуск в режиме Telegram-бота. Ctrl+C для остановки.");
        await bot.RunAsync(cts.Token);
    }
    else
    {
        ConsoleUI ui = provider.GetRequiredService<ConsoleUI>();
        await ui.RunAsync(cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nОстановлено пользователем.");
}
