using Hercules.CodeExecution.SkillSdkAdapters;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Memory.Layers;
using Hercules.SkillSdk;
using Hercules.Tools;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution;

/// <summary>
///     Agent-side aggregate implementation of <see cref="IHerculesSkillContext"/>.
///     Created per skill execution and scoped to one skill run.
/// </summary>
public sealed class HerculesSkillContext : SkillContextBase
{
    private readonly HttpConfig _httpConfig;
    private readonly Dictionary<string, string> _safeConfig;

    public override IHttpClient Http { get; }
    public override IMcpClient Mcp { get; }
    public override ILlmClient Llm { get; }
    public override IMemoryClient Memory { get; }
    public override ISkillLogger Logger { get; }
    public override ISessionContext Session { get; }

    public HerculesSkillContext(
        string sessionId,
        string? userId,
        HttpConfig httpConfig,
        ILLMClient llmClient,
        ToolRegistry toolRegistry,
        LayeredMemoryManager layeredMemory,
        IDurableFactsStore factsStore,
        ILogger logger,
        IHttpClientFactory? httpFactory = null,
        string? skillName = null,
        CancellationToken cancellationToken = default)
    {
        _httpConfig = httpConfig;

        Session = new SessionContext(sessionId, userId, new Dictionary<string, string>(), cancellationToken);
        Logger = new SkillLoggerAdapter(logger, skillName ?? "skill");
        Http = new HttpClientAdapter(httpConfig, logger, httpFactory);
        Mcp = new McpClientAdapter(toolRegistry, logger);
        Llm = new LlmClientAdapter(llmClient, logger);
        Memory = new MemoryClientAdapter(sessionId, layeredMemory, factsStore, logger);

        _safeConfig = BuildSafeConfig(httpConfig);
    }

    public override string? GetConfig(string keyPath)
    {
        return _safeConfig.TryGetValue(keyPath, out var value) ? value : null;
    }

    private static Dictionary<string, string> BuildSafeConfig(HttpConfig httpConfig)
    {
        // Only non-secret, safe config values are exposed to file-based skills.
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Http.TimeoutSeconds"] = httpConfig.TimeoutSeconds.ToString(),
            ["Http.RateLimitPerMinute"] = httpConfig.RateLimitPerMinute.ToString(),
            ["Http.MaxResponseSizeKb"] = httpConfig.MaxResponseSizeKb.ToString()
        };
    }
}
