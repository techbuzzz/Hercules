using System.Text;
using Hercules.Config;
using Hercules.Tools.Policy;
using Microsoft.Extensions.Logging;

namespace Hercules.Tools;

/// <summary>
///     Реестр инструментов, доступных агенту.
///     DI singleton — все зарегистрированные <see cref="ITool" /> агрегируются здесь.
///     LLM получает список доступных tools через <see cref="ListForLLM" /> (для prompt injection).
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<IEnumerable<ITool>> _toolsFactory;
    private readonly ToolPolicyEngine? _policy;

    public ToolRegistry(IEnumerable<ITool> tools, ILoggerFactory loggerFactory, ToolPolicyEngine? policy = null)
    {
        _loggerFactory = loggerFactory;
        _toolsFactory = () => tools;
        _policy = policy;
        Register(tools);
    }

    /// <summary>Все зарегистрированные tool'ы (для итерации/диагностики).</summary>
    public IReadOnlyCollection<ITool> All => _tools.Values;

    /// <summary>Список имён (lowercase).</summary>
    public IReadOnlyCollection<string> Names => _tools.Keys;

    /// <summary>
    ///     Перезагрузить список инструментов при runtime-изменении конфигурации.
    ///     Некоторые инструменты (MCP, HTTP) зависят от конфигурации и должны быть
    ///     пересозданы с новыми настройками.
    /// </summary>
    public void Reload(AppConfig cfg)
    {
        _tools.Clear();
        IEnumerable<ITool> fresh = _toolsFactory().Select(t => t switch
        {
            HttpTool => new HttpTool(cfg.Http, _loggerFactory.CreateLogger<HttpTool>()),
            A2AClient => new A2AClient(cfg.A2A),
            _ => t
        });
        Register(fresh);
    }

    private void Register(IEnumerable<ITool> tools)
    {
        foreach (ITool t in tools)
        {
            if (string.IsNullOrWhiteSpace(t.Name))
            {
                throw new InvalidOperationException("Tool name cannot be empty");
            }

            if (_tools.ContainsKey(t.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name: '{t.Name}' (existing: {_tools[t.Name].GetType().Name}, new: {t.GetType().Name})");
            }

            _tools[t.Name] = t;

            // Register tool with policy engine
            _policy?.Register(t);
        }
    }

    /// <summary>Получить tool по имени (case-insensitive). Null если не найден.</summary>
    public ITool? Get(string name)
    {
        return _tools.TryGetValue(name, out ITool? t)
            ? t
            : null;
    }

    /// <summary>
    ///     Сгенерировать markdown-секцию "Available Tools" для system prompt.
    ///     LLM использует её чтобы понять, какие actions доступны.
    /// </summary>
    public string ListForLLM()
    {
        if (_tools.Count == 0)
        {
            return "(no tools available)";
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Available Tools");
        sb.AppendLine();
        sb.AppendLine("Ты можешь вызвать инструмент, вернув JSON в формате:");
        sb.AppendLine("```json");
        sb.AppendLine("{\"action\": \"tool_name\", \"arguments\": {...}}");
        sb.AppendLine("```");
        sb.AppendLine();
        foreach (ITool t in _tools.Values)
        {
            sb.AppendLine($"### {t.Name}");
            sb.AppendLine($"{t.Description}");
            if (!string.IsNullOrWhiteSpace(t.ParametersSchema))
            {
                sb.AppendLine();
                sb.AppendLine("Parameters (JSON schema):");
                sb.AppendLine("```json");
                sb.AppendLine(t.ParametersSchema);
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
