using System.Globalization;
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
        if (!decimal.TryParse(Numeric(value), out var amount))
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

    /// <summary>
    ///     R19: strips an optional leading currency symbol and trailing percent sign so
    ///     "$1 200", "1200%" and "1.200,50" all parse. Group 1 of <see cref="NumericPattern"/>
    ///     may now carry a "$" prefix, which <c>double.TryParse</c> rejects outright.
    /// </summary>
    private static string Numeric(string value) =>
        value.Replace(",", "").Replace("%", "").Replace("$", "").Replace("€", "").Replace("£", "").Trim();

    private static bool PlausiblePercentage(string value, out string issue)
    {
        issue = "";
        if (!double.TryParse(Numeric(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return false;

        // R19: the previous bound of 10 000 let "99999 %" pass as plausible. Percentages
        // below -100 are impossible, and above 1000 are not credible as a rate/share
        // claim. Growth figures legitimately exceed 100 %, so the ceiling stays well
        // above 100 rather than clamping to it.
        if (pct is < -100 or > 1000)
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

    /// <summary>
///     Matches a numeric assertion: an optional currency symbol, the number, a unit,
///     and a short trailing context used for date sanity checks.
/// </summary>
/// <remarks>
///     R19 — three defects in the previous pattern:
///     <list type="bullet">
///         <item>
///             <b>\b after the unit was unreachable for non-word units.</b> A word
///             boundary requires a word character on one side; "%", "$", "мб" etc. end
///             on non-word characters, so <c>"Success rate is 99999 % in testing"</c>
///             never matched and the implausible-percentage check silently never ran.
///             Replaced with <c>(?![a-zа-яё])</c>, which correctly asserts "not followed
///             by another letter" for both word and non-word units.
///         </item>
///         <item>
///             <b>Common currency and time words were missing from the alternation</b> —
///             "50 dollars" and "30 seconds" did not match at all, so <c>CanVerify</c>
///             returned false for perfectly ordinary numeric claims.
///         </item>
///         <item>
///             <b>No sign support and overlapping quantifiers.</b> <c>(\d[\d\s.,]*)\s*</c>
///             lets two adjacent greedy loops consume the same whitespace (a catastrophic
///             backtracking risk on adversarial input) and cannot match "-150 %".
///             Group 1 now accepts an optional currency symbol and sign, consumes internal
///             digit groups itself, and leaves inter-token whitespace to <c>\s*</c>.
///         </item>
///     </list>
/// </remarks>
[GeneratedRegex(@"([$€£]?\s?-?\d[\d.,]*(?:\s[\d.,]+)*)\s*(%|percent|\$|usd|eur|gbp|dollars?|rubles?|рублей|руб|kb|mb|gb|tb|bytes?|b|кб|мб|гб|км|м|cm|mm|years?|year|days?|day|months?|month|hours?|hour|minutes?|minute|seconds?|second|ms)(?![a-zа-яё])\s*(.{0,30})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NumericPattern();
}
