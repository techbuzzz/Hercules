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

// LLM-слой (отказоустойчивый клиент с fallback)
services.AddSingleton<LlmClientFactory>();
services.AddSingleton<ILLMClient>(sp =>
    new ResilientLLMClient(sp.GetRequiredService<LlmConfig>(), sp.GetRequiredService<LlmClientFactory>()));

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
