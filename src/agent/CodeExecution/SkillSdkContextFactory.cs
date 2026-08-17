using Hercules.Config;
using Hercules.LLM;
using Hercules.Memory.Layers;
using Hercules.SkillSdk;
using Hercules.Tools;
using Microsoft.Extensions.Logging;

namespace Hercules.CodeExecution;

/// <summary>
///     Default agent implementation of <see cref="ISkillContextFactory"/>.
///     Builds a <see cref="HerculesSkillContext"/> from agent services.
/// </summary>
public sealed class SkillSdkContextFactory : ISkillContextFactory
{
    private readonly HttpConfig _httpConfig;
    private readonly ILLMClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly LayeredMemoryManager _layeredMemory;
    private readonly IDurableFactsStore _factsStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory? _httpFactory;

    public SkillSdkContextFactory(
        HttpConfig httpConfig,
        ILLMClient llmClient,
        ToolRegistry toolRegistry,
        LayeredMemoryManager layeredMemory,
        IDurableFactsStore factsStore,
        ILoggerFactory loggerFactory,
        IHttpClientFactory? httpFactory = null)
    {
        _httpConfig = httpConfig;
        _llmClient = llmClient;
        _toolRegistry = toolRegistry;
        _layeredMemory = layeredMemory;
        _factsStore = factsStore;
        _loggerFactory = loggerFactory;
        _httpFactory = httpFactory;
    }

    public IHerculesSkillContext Create(string sessionId, string? userId, string skillName, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger($"Skill.{skillName}");
        return new HerculesSkillContext(
            sessionId,
            userId,
            _httpConfig,
            _llmClient,
            _toolRegistry,
            _layeredMemory,
            _factsStore,
            logger,
            _httpFactory,
            skillName,
            cancellationToken);
    }
}
