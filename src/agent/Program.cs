using System.Text;
using Hercules.Agent;
using Hercules.CLI;
using Hercules.CodeExecution;
using Hercules.Config;
using Hercules.LLM;
using Hercules.LLM.JsonRepair;
using Hercules.Mesh;
using Hercules.Skills;
using Hercules.Storage;
using Hercules.Telegram;
using Hercules.Tools;
using Hercules.Tools.Policy;
using Hercules.WasmSandbox;
using Hercules.WasmSandbox.Compilation;
using HerculesBus;
using HerculesBus.Core;
using HerculesBus.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================================
//  Hercules — самообучающийся микроагент (C# 15 / .NET 10)
//  Точка входа: настройка конфигурации, DI и запуск выбранного интерфейса.
// ============================================================================

// Поддержка корректного отображения кириллицы
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureAppConfiguration(config =>
{
    config.SetBasePath(AppContext.BaseDirectory)
        .AddUserSecrets<Program>()
        .AddJsonFile("appsettings.json", false, false)
        .AddEnvironmentVariables("HERCULES_");
});

builder.ConfigureServices((context, services) =>
{
    var appConfig = context.Configuration.Get<AppConfig>() ?? new AppConfig();

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
    services.AddSingleton(appConfig.Mesh);
    services.AddSingleton(appConfig.ToolPolicy);

    // LLM-слой (отказоустойчивый клиент с fallback + multi-role routing v2)
    services.AddSingleton<LlmClientFactory>();
    services.AddSingleton<RoleRouter>();
    services.AddSingleton<IJsonRepairService, JsonRepairService>();
    services.AddSingleton<ResilientLLMClient>(sp =>
        new ResilientLLMClient(
            sp.GetRequiredService<LlmConfig>(),
            sp.GetRequiredService<LlmClientFactory>(),
            sp.GetRequiredService<RoleRouter>(),
            sp.GetRequiredService<ILogger<ResilientLLMClient>>()));
    services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<ResilientLLMClient>());
    services.AddSingleton<ProviderHealthChecker>();
    services.AddSingleton<ProviderCapabilityDetector>();

    // Code execution (Stage 2, v2)
    services.AddSingleton<SandboxOptions>(sp =>
    {
        var cfg = sp.GetRequiredService<CodeExecutionConfig>();
        var opts = new SandboxOptions
        {
            CpuTimeoutSeconds = cfg.CpuTimeoutSeconds,
            MaxFileSizeMb = cfg.MaxFileSizeMb,
            MaxProcesses = cfg.MaxProcesses,
            MaxOpenFiles = cfg.MaxOpenFiles,
            MaxVirtualMemoryMb = cfg.MaxVirtualMemoryMb,
            AllowNetwork = cfg.AllowNetwork,
            MaxCodeSizeKb = cfg.MaxCodeSizeKb,
            SessionTtlSeconds = cfg.SessionTtlSeconds
        };
        if (!string.IsNullOrWhiteSpace(cfg.TempRoot))
        {
            opts.TempRoot = cfg.TempRoot;
        }

        return opts;
    });
    services.AddSingleton<ICodeExecutor, DotnetFileBasedExecutor>();

    // Tool ecosystem (Stage 3, v2)
    services.AddSingleton<ITool, HttpTool>();
    services.AddSingleton<ITool, A2AClient>();
    services.AddSingleton<ITool, CodeExecutionTool>();
    // Tool policy engine (task_009) — registered before ToolRegistry so it can be injected
    services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<ToolPolicyConfig>();
        var perms = ToolPermissionExtensions.ParseFromString(cfg.AgentPermissions);
        return new ToolPermissionSet(perms);
    });
    services.AddSingleton<ToolPolicyEngine>(sp =>
        new ToolPolicyEngine(
            sp.GetRequiredService<ToolPolicyConfig>(),
            sp.GetRequiredService<ToolPermissionSet>(),
            sp.GetRequiredService<ILogger<ToolPolicyEngine>>()));
    services.AddSingleton<ToolRegistry>();
    services.AddSingleton<McpClient>();

    // WASM sandbox (v3)
    services.AddSingleton<IWasmSandbox, WasmtimeSandbox>();
    services.AddSingleton<CompilerRegistry>(sp =>
    {
        var registry = new CompilerRegistry();
        registry.Register(new PassthroughCompiler());
        return registry;
    });
    services.AddSingleton<WasmTool>();
    services.AddSingleton<ITool, WasmToolAdapter>();

    // HerculesBus (v3.1)
    services.AddSingleton<IChannelStore, InMemoryChannelStore>();
    services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
    services.AddSingleton<IEventBus, InMemoryEventBus>();
    services.AddSingleton<Bus>();

    // Phase 3: Inter-agent mesh
    services.AddMeshServices(appConfig.Mesh, appConfig.Storage.DataRoot);

    // Хранилища
    services.AddSingleton<FileSkillRepository>();
    services.AddSingleton<MemoryStore>();
    services.AddSingleton<SqliteSessionStore>();

    // Hybrid storage services (task_003)
    services.AddSingleton<IBudgetService, BudgetService>();
    services.AddSingleton<IAuditLog, AuditLogService>();

    // Phase 2: Skill packager
    services.AddSingleton<SkillPackager>();

    // Phase 2: Semantic routing
    services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();
    services.AddSingleton<EmbeddingSkillRouter>();

    // Phase 2: Skill marketplace + agent templates
    services.AddSingleton<SkillMarketplace>();
    services.AddSingleton<AgentTemplateManager>();

    // Skill lifecycle (task_005)
    services.AddSingleton<SkillLifecyclePolicy>();
    services.AddSingleton<SkillDeprecationManager>();
    services.AddSingleton<SkillEvaluationEngine>();
    services.AddSingleton<SkillLifecycleService>();

    // Агент
    services.AddSingleton<SkillManager>();
    services.AddSingleton<SkillRouter>();
    services.AddSingleton<MemoryManager>();
    services.AddSingleton<ReflectionEngine>();
    services.AddSingleton<AgentCore>();

    // Интерфейсы
    services.AddSingleton<ConsoleUI>();
    services.AddSingleton<TelegramBotInterface>();
});

using var host = builder.Build();

var appConfig = host.Services.GetRequiredService<AppConfig>();

// --- Выбор режима запуска ---
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var telegramMode = args.Contains("--telegram") || (appConfig.Telegram.Enabled && args.Contains("--bot"));
var benchmarkMode = args.Contains("--benchmark");

try
{
    if (benchmarkMode)
    {
        Console.WriteLine("Hercules Benchmark Suite");
        Console.WriteLine("=======================\n");

        var agent = host.Services.GetRequiredService<AgentCore>();
        var skills = host.Services.GetRequiredService<SkillManager>();
        var sessions = host.Services.GetRequiredService<SqliteSessionStore>();
        var budget = (BudgetService)host.Services.GetRequiredService<IBudgetService>();
        var memory = host.Services.GetRequiredService<MemoryStore>();

        var runner = new BenchmarkRunner(agent, skills, sessions, budget, memory);
        await runner.RunAsync(ct: cts.Token);
    }
    else if (telegramMode)
    {
        TelegramBotInterface bot = host.Services.GetRequiredService<TelegramBotInterface>();
        Console.WriteLine("Запуск в режиме Telegram-бота. Ctrl+C для остановки.");
        await bot.RunAsync(cts.Token);
    }
    else
    {
        ConsoleUI ui = host.Services.GetRequiredService<ConsoleUI>();
        await ui.RunAsync(cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nОстановлено пользователем.");
}