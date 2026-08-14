using System.Text.Json.Serialization;
using HerculesBus.Core;

namespace Hercules.Config.Rollout;

/// <summary>
///     Стадия rollout-канала (task_058).
/// </summary>
public enum BundleStage
{
    /// <summary>Бандл загружен, но не активирован.</summary>
    Pending,

    /// <summary>Бандл применяется к staging-группе агентов.</summary>
    Staging,

    /// <summary>Бандл прошёл staging и продвинут в production.</summary>
    Production,

    /// <summary>Бандл истёк или откачен.</summary>
    Retired
}

/// <summary>
///     Статус валидации подписанного бандла.
/// </summary>
public sealed record BundleValidationResult(
    bool IsValid,
    bool IsTrusted,
    string? SignerId,
    DateTime? SignedAt,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static BundleValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, false, null, null, errors, Array.Empty<string>());

    public static BundleValidationResult Success(string? signerId, DateTime? signedAt, IReadOnlyList<string> warnings = null!) =>
        new(true, signerId != null, signerId, signedAt, Array.Empty<string>(), warnings ?? Array.Empty<string>());
}

/// <summary>
///     Подписанный бандл конфигурации или policy.
/// </summary>
public sealed class ConfigBundle
{
    /// <summary>Уникальный ID бандла (ULID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Ulid.NewId();

    /// <summary>Версия бандла (semver).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Тип бандла: "config" или "policy".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "config";

    /// <summary>Имя бандла.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Описание бандла.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Кем подписан бандл.</summary>
    [JsonPropertyName("signerId")]
    public string? SignerId { get; set; }

    /// <summary>Когда подписан.</summary>
    [JsonPropertyName("signedAt")]
    public DateTime? SignedAt { get; set; }

    /// <summary>Алгоритм подписи.</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "HMAC-SHA256";

    /// <summary>Base64-encoded подпись содержимого.</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    /// <summary>Публичная подпись (fingerprint ключа подписанта).</summary>
    [JsonPropertyName("publicKeyFingerprint")]
    public string? PublicKeyFingerprint { get; set; }

    /// <summary>Метаданные о staging-группе.</summary>
    [JsonPropertyName("stagingGroup")]
    public string? StagingGroup { get; set; }

    /// <summary>TTL бандла — после этой даты бандл не применяется.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Минимальная версия Hercules для совместимости.</summary>
    [JsonPropertyName("minHerculesVersion")]
    public string? MinHerculesVersion { get; set; }

    /// <summary>Максимальная версия Hercules для совместимости.</summary>
    [JsonPropertyName("maxHerculesVersion")]
    public string? MaxHerculesVersion { get; set; }

    /// <summary>Текущая стадия rollout.</summary>
    [JsonPropertyName("stage")]
    public BundleStage Stage { get; set; } = BundleStage.Pending;

    /// <summary>Когда бандл был создан.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда бандл был применён (stage = Production).</summary>
    [JsonPropertyName("appliedAt")]
    public DateTime? AppliedAt { get; set; }

    /// <summary>Время жизни на staging (минуты).</summary>
    [JsonPropertyName("stagingDurationMinutes")]
    public int StagingDurationMinutes { get; set; } = 60;

    /// <summary>Payload конфигурации (JSON).</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "{}";
}

/// <summary>
///     Состояние rollout на данный момент.
/// </summary>
public sealed class RolloutState
{
    [JsonPropertyName("currentBundleId")]
    public string? CurrentBundleId { get; set; }

    [JsonPropertyName("currentVersion")]
    public string? CurrentVersion { get; set; }

    [JsonPropertyName("currentStage")]
    public BundleStage CurrentStage { get; set; }

    [JsonPropertyName("lastKnownGoodBundleId")]
    public string? LastKnownGoodBundleId { get; set; }

    [JsonPropertyName("lastKnownGoodVersion")]
    public string? LastKnownGoodVersion { get; set; }

    [JsonPropertyName("pendingBundleId")]
    public string? PendingBundleId { get; set; }

    [JsonPropertyName("pendingVersion")]
    public string? PendingVersion { get; set; }

    [JsonPropertyName("lastPromotedAt")]
    public DateTime? LastPromotedAt { get; set; }

    [JsonPropertyName("lastRollbackAt")]
    public DateTime? LastRollbackAt { get; set; }

    [JsonPropertyName("rolloutHistory")]
    public List<RolloutHistoryEntry> History { get; set; } = new();
}

/// <summary>
///     Запись в истории rollout.
/// </summary>
public sealed class RolloutHistoryEntry
{
    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = ""; // "applied", "promoted", "rolled_back", "expired"

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
///     Результат применения бандла.
/// </summary>
public sealed record RolloutResult(
    bool Success,
    string? Error,
    string? AppliedBundleId,
    BundleStage NewStage,
    string? RollbackTriggered);
