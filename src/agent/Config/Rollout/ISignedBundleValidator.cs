namespace Hercules.Config.Rollout;

/// <summary>
///     Валидатор подписанных config/policy бандлов (task_058).
///     Проверяет HMAC-SHA256 подпись и optionally trusted signer list.
/// </summary>
public interface ISignedBundleValidator
{
    /// <summary>
    ///     Валидировать подпись бандла и опционально проверить trusted signers.
    /// </summary>
    Task<BundleValidationResult> ValidateAsync(ConfigBundle bundle, CancellationToken ct = default);

    /// <summary>
    ///     Подписать бандл от имени агента и вернуть обновлённый бандл с подписью.
    /// </summary>
    Task<ConfigBundle> SignBundleAsync(ConfigBundle bundle, CancellationToken ct = default);
}
