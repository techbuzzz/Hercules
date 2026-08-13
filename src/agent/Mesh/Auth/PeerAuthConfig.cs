using System.Text.Json.Serialization;

namespace Hercules.Mesh.Auth;

/// <summary>
///     Конфигурация inter-agent auth (task_039).
///     Управляет secret для подписи bearer-токенов, TTL, max delegation depth,
///     и per-peer API keys.
/// </summary>
public sealed class PeerAuthConfig
{
    /// <summary>
    ///     Включить проверку auth на inbound `/api/mesh/*` запросы.
    ///     Если false — middleware пропускает запросы (но tool policy всё равно применяется).
    ///     Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Secret для подписи/проверки bearer-токенов (HMAC-SHA256).
    ///     ВАЖНО: должен быть ≥ 32 байт случайных данных. Можно задать через env:HERCULES_SECRET_MESH_TOKEN_SECRET.
    ///     Если пусто — генерируется автоматически при старте (не переживает restart).
    /// </summary>
    public string TokenSecret { get; set; } = "";

    /// <summary>TTL выпускаемых bearer-токенов в секундах. Default: 300 (5 минут).</summary>
    public int TokenTtlSeconds { get; set; } = 300;

    /// <summary>Максимальная глубина делегации (защита от циклов). Default: 3.</summary>
    public int MaxDelegationDepth { get; set; } = 3;

    /// <summary>
    ///     API-ключи per-peer. Ключ — AgentId peer'а, значение — секрет.
    ///     Используется когда <see cref="PeerAuthMode"/> = "apikey".
    ///     ВАЖНО: секреты должны приходить из env vars (env:MY_PEER_KEY) или vault.
    /// </summary>
    public Dictionary<string, string> PeerKeys { get; set; } = new();

    /// <summary>
    ///     Режим auth для outbound вызовов: "bearer" (дефолт) или "apikey".
    ///     mTLS поддерживается только когда оба агента работают с mTLS-инфраструктурой.
    /// </summary>
    public string OutboundMode { get; set; } = "bearer";

    /// <summary>
    ///     Список scope, выпускаемых по умолчанию при вызове через token issuer.
    ///     "delegate:read" — read-only delegate; "delegate:write" — full delegate.
    /// </summary>
    public List<string> DefaultScopes { get; set; } = new() { "delegate:read" };
}

/// <summary>
///     Конфигурация одного peer'а для outbound auth (расширение MeshPeerConfig).
///     Добавлено в MeshPeerConfig.Auth поле для обратной совместимости.
/// </summary>
public sealed class PeerAuthOverride
{
    /// <summary>Per-peer API key (если OutboundMode=apikey). Можно задать как env:VAR_NAME.</summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    /// <summary>Per-peer auth mode override (если хочется отличать от глобального).</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}
