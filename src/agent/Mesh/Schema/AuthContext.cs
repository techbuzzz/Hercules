using System.Text.Json.Serialization;

namespace Hercules.Mesh.Schema;

/// <summary>
///     Auth context для inter-agent delegation.
///     Передаётся от sender к recipient и позволяет:
///     - аутентифицировать вызывающего (токен/JWT)
///     - передавать identity claims
///     - отслеживать глубину делегации (для защиты от циклов)
/// </summary>
public sealed class AuthContext
{
    /// <summary>Текущая версия auth context.</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>
    ///     Тип аутентификации: "bearer" | "apikey" | "mtls" | "none".
    /// </summary>
    [JsonPropertyName("auth_type")]
    public string AuthType { get; set; } = "bearer";

    /// <summary>
    ///     Bearer token, API key, или null для mtls/none.
    ///     ВАЖНО: не логируем и не сохраняем в audit trail полные токены.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>
    ///     Identity claims: subject, issuer, audience, scope, etc.
    ///     Пример: { "sub": "agent/hercules", "iss": "mesh-root", "scope": "delegate:read" }
    /// </summary>
    [JsonPropertyName("claims")]
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>
    ///     Глубина делегации. Sender устанавливает 0 или копирует от предыдущего уровня + 1.
    ///     Recipient отклоняет, если depth > maxDelegationDepth (настраивается в policy).
    /// </summary>
    [JsonPropertyName("delegation_depth")]
    public int DelegationDepth { get; set; } = 0;

    /// <summary>
    ///     ID исходного вызова (первый sender в цепочке).
    ///     Используется для audit trail.
    /// </summary>
    [JsonPropertyName("root_request_id")]
    public string? RootRequestId { get; set; }

    /// <summary>
    ///     Версия auth context (semver).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = CurrentVersion;

    /// <summary>
    ///     Создать anonymous auth context (без аутентификации).
    /// </summary>
    public static AuthContext Anonymous()
    {
        return new AuthContext { AuthType = "none", Token = null };
    }

    /// <summary>
    ///     Создать bearer auth context.
    /// </summary>
    public static AuthContext Bearer(string token, int delegationDepth = 0, string? rootRequestId = null)
    {
        return new AuthContext
        {
            AuthType = "bearer",
            Token = token,
            DelegationDepth = delegationDepth,
            RootRequestId = rootRequestId
        };
    }

    /// <summary>
    ///     Создать следующий уровень делегации (increments depth).
    /// </summary>
    public AuthContext WithIncrementedDepth()
    {
        return new AuthContext
        {
            AuthType = AuthType,
            Token = Token,
            Claims = new Dictionary<string, string>(Claims),
            DelegationDepth = DelegationDepth + 1,
            RootRequestId = RootRequestId ?? RootRequestId
        };
    }

    /// <summary>
    ///     Проверить, не превышена ли максимальная глубина делегации.
    /// </summary>
    public bool IsWithinDepthLimit(int maxDepth)
    {
        return DelegationDepth <= maxDepth;
    }

    /// <summary>
    ///     Получить claim по ключу.
    /// </summary>
    public string? GetClaim(string key)
    {
        return Claims.TryGetValue(key, out var value) ? value : null;
    }
}
