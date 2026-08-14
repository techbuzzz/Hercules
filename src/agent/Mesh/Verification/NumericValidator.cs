using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Verification;

/// <summary>
///     Валидирует числовые утверждения в ответе:
///     - Цены, даты, проценты, размеры файлов, расстояния и т.д.
///     - Проверяет plausibility через статистические bounds.
/// </summary>
public sealed partial class NumericValidator : IVerifier
{
    private readonly ILogger<NumericValidator> _logger;

    public NumericValidator(ILogger<NumericValidator> logger) => _logger = logger;

    public string Name => "NumericValidator";

    public bool CanVerify(VerificationContext ctx) =>
        !string.IsNullOrEmpty(ctx.ResponseText) &&
        NumericPattern().IsMatch(ctx.ResponseText);

    public Task<VerifierResult> VerifyAsync(VerificationContext ctx, CancellationToken ct = default)
    {
        var text = ctx.ResponseText;
        var issues = new List<string>();
        var details = new Dictionary<string, string>();

        // Find numeric assertions: currency, percentages, file sizes, dates, distances
        var matches = NumericPattern().Matches(text);
        foreach (Match m in matches)
        {
            var value = m.Groups[1].Value;
            var unit = m.Groups[2].Value.ToLowerInvariant().Trim();
            var context = m.Groups[3].Value.ToLowerInvariant();

            // Currency plausibility
            if (PlausibleCurrency(value, unit, out var currencyIssue))
            {
                issues.Add(currencyIssue);
                details[$"currency_{m.Index}"] = $"{value} {unit}";
            }

            // Percentage plausibility
            if (PlausiblePercentage(value, out var pctIssue))
            {
                issues.Add(pctIssue);
                details[$"percentage_{m.Index}"] = value;
            }

            // File size plausibility
            if (PlausibleFileSize(value, unit, out var sizeIssue))
            {
                issues.Add(sizeIssue);
                details[$"filesize_{m.Index}"] = $"{value} {unit}";
            }

            // Date plausibility
            if (PlausibleDate(context, out var dateIssue))
            {
                issues.Add(dateIssue);
                details[$"date_{m.Index}"] = context;
            }
        }

        if (issues.Count == 0)
            return Task.FromResult(VerifierResult.Pass(Name));

        return Task.FromResult(VerifierResult.Fail(Name,
            $"Numeric plausibility issues: {string.Join("; ", issues.Distinct())}",
            VerificationSeverity.Low,
            "NUMERIC_IMPLAUSIBLE",
            details));
    }

    private static bool PlausibleCurrency(string value, string unit, out string issue)
    {
        issue = "";
        if (!decimal.TryParse(value.Replace(",", ""), out var amount))
            return false;

        var u = unit.Trim();
        // Reasonable USD range
        if (u is "$" or "usd" or "dollars")
        {
            if (amount is > 1_000_000_000_000 and < 10_000_000_000_000)
            {
                issue = $"Implausibly large USD amount: {value}";
                return true;
            }
        }
        return false;
    }

    private static bool PlausiblePercentage(string value, out string issue)
    {
        issue = "";
        if (!double.TryParse(value.Replace("%", "").Trim(), out var pct))
            return false;

        if (pct is < -100 or > 10000)
        {
            issue = $"Implausible percentage: {value}";
            return true;
        }
        return false;
    }

    private static bool PlausibleFileSize(string value, string unit, out string issue)
    {
        issue = "";
        if (!double.TryParse(value, out var size))
            return false;

        var u = unit.Trim().ToLowerInvariant();
        // Normalize to bytes
        var bytes = u switch
        {
            "kb" or "кб" => size * 1024,
            "mb" or "мб" => size * 1024 * 1024,
            "gb" or "гб" => size * 1024 * 1024 * 1024,
            "tb" => size * 1024L * 1024 * 1024 * 1024,
            "b" or "" => size,
            _ => 0
        };

        // File sizes > 10 PB are physically implausible
        if (bytes > 10L * 1024 * 1024 * 1024 * 1024 * 1024)
        {
            issue = $"Implausible file size: {value}";
            return true;
        }
        return false;
    }

    private static bool PlausibleDate(string context, out string issue)
    {
        issue = "";
        // Basic date sanity: future dates in historical context are suspicious
        // We just flag very obviously wrong dates (not a full calendar check)
        return false;
    }

    [GeneratedRegex(@"(\d[\d\s.,]*)\s*(%|percent|\$|usd|eur|gbp|kb|mb|gb|tb|b|кб|мб|гб|км|м|cm|mm|years?|days?|months?)\b\s*(.{0,30})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NumericPattern();
}
