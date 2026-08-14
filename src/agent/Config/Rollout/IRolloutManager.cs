namespace Hercules.Config.Rollout;

/// <summary>
///     Менеджер staged rollout конфигурационных и policy бандлов (task_058).
///     Управляет Pending → Staging → Production → Retired lifecycle.
/// </summary>
public interface IRolloutManager
{
    /// <summary>Текущее состояние rollout.</summary>
    RolloutState GetState();

    /// <summary>Загрузить и применить бандл (проверяет подпись + валидацию).</summary>
    Task<RolloutResult> ApplyBundleAsync(ConfigBundle bundle, CancellationToken ct = default);

    /// <summary>Продвинуть pending/staging бандл на следующую стадию.</summary>
    Task<RolloutResult> PromoteStageAsync(string bundleId, CancellationToken ct = default);

    /// <summary>Откатиться к last-known-good бандлу.</summary>
    Task<RolloutResult> RollbackAsync(string? reason = null, CancellationToken ct = default);

    /// <summary>Проверить, не истёк ли pending/staging бандл.</summary>
    void CheckExpiry();

    /// <summary>Получить бандл по ID из хранилища.</summary>
    ConfigBundle? GetBundle(string bundleId);
}
