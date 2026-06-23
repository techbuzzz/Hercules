using Hercules.Config;

namespace Hercules.LLM;

/// <summary>
///     Маршрутизатор LLM-ролей (multi-role routing, v2).
///     По имени роли возвращает ILLMClient, сконфигурированный для этой роли.
///     Если роль не сконфигурирована — возвращает fallback-клиент (Roles.Main).
/// </summary>
public sealed class RoleRouter
{
    private readonly LlmClientFactory _factory;
    private readonly Dictionary<string, RoleConfig> _roles;
    private readonly string _defaultProvider;
    private readonly Dictionary<string, ILLMClient> _cache = new(StringComparer.OrdinalIgnoreCase);
    private ILLMClient? _mainFallback;

    public RoleRouter(AppConfig appConfig, LlmClientFactory factory)
    {
        _factory = factory;
        _roles = appConfig.Roles ?? new Dictionary<string, RoleConfig>(StringComparer.OrdinalIgnoreCase);
        _defaultProvider = appConfig.Llm.Provider;
    }

    /// <summary>
    ///     Получить клиент для роли. Кэширует созданные клиенты по имени роли.
    ///     Если роль не сконфигурирована — возвращает клиент main-роли (fallback).
    /// </summary>
    public ILLMClient Resolve(string role)
    {
        if (string.IsNullOrEmpty(role) || role == Roles.Main)
        {
            return GetOrCreateMain();
        }

        if (_cache.TryGetValue(role, out var cached))
        {
            return cached;
        }

        if (_roles.TryGetValue(role, out var roleCfg))
        {
            var provider = string.IsNullOrWhiteSpace(roleCfg.Provider)
                ? _defaultProvider
                : roleCfg.Provider;
            var client = _factory.Create(provider);
            _cache[role] = client;
            return client;
        }

        // Не сконфигурировано — fallback на main
        return GetOrCreateMain();
    }

    /// <summary>Получить клиент main-роли (default provider из Llm.Provider).</summary>
    public ILLMClient GetOrCreateMain()
    {
        return _mainFallback ??= _factory.Create(_defaultProvider);
    }

    /// <summary>Список сконфигурированных ролей (для диагностики / API).</summary>
    public IReadOnlyCollection<string> ConfiguredRoles => _roles.Keys;
}
