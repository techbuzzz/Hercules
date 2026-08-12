using Hercules.Config;
using Microsoft.Extensions.Logging;

namespace Hercules.Budget;

/// <summary>
///     Хелпер для формирования graceful degradation ответов при guardrail-violations.
///     Используется в AgentCore для преобразования GuardrailViolation в текст ответа.
/// </summary>
public sealed class BudgetGuard
{
    private readonly BudgetConfig _cfg;
    private readonly ILogger<BudgetGuard> _logger;

    public BudgetGuard(BudgetConfig cfg, ILogger<BudgetGuard> logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    /// <summary>
    ///     Проверить лимиты и вернуть degradation message, если есть hard-cap violation.
    ///     Возвращает null, если можно продолжать.
    /// </summary>
    public string? CheckAndGetDegradationMessage(GuardrailCheckResult result)
    {
        if (!_cfg.Enabled)
            return null;

        var hardViolations = result.Violations
            .Where(v => v.EnforcementMode == "hard_cap" && v.Actual > v.Limit)
            .ToList();

        if (hardViolations.Count == 0)
            return null;

        var lines = hardViolations.Select(v => $"  — {v.Message}");
        var msg = $"⚠️ Превышен лимит безопасности:\n{string.Join("\n", lines)}\n\n" +
                   "Запрос остановлен. Попробуйте упростить задачу или увеличить лимиты в конфигурации.";

        _logger.LogWarning("[BudgetGuard] Hard-cap violation(s): {Violations}",
            string.Join("; ", hardViolations.Select(v => v.Message)));

        return msg;
    }

    /// <summary>
    ///     Логировать soft-warn violations (не блокирующие).
    /// </summary>
    public void LogSoftWarnings(GuardrailCheckResult result)
    {
        if (!_cfg.Enabled)
            return;

        var soft = result.Violations
            .Where(v => v.EnforcementMode == "soft_warn" && v.Actual > v.Limit)
            .ToList();

        foreach (var v in soft)
        {
            _logger.LogWarning("[BudgetGuard] Soft-warn: {Message}", v.Message);
        }
    }

    /// <summary>
    ///     Проверить, достаточно ли tokens для выполнения запроса (предварительная проверка).
    /// </summary>
    public string? CheckTokensBeforeRequest(int estimatedTokens)
    {
        if (!_cfg.Enabled)
            return null;

        if (_cfg.MaxTokensPerRequest > 0 && estimatedTokens > _cfg.MaxTokensPerRequest)
        {
            var msg = $"⚠️ Запрос требует ~{estimatedTokens} токенов, лимит — {_cfg.MaxTokensPerRequest}. " +
                      "Уменьшите входные данные или увеличьте MaxTokensPerRequest в конфигурации.";
            _logger.LogWarning("[BudgetGuard] {Message}", msg);
            return msg;
        }

        return null;
    }
}
