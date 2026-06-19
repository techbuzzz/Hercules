using System.Text.RegularExpressions;

namespace Hercules.CodeExecution;

/// <summary>
///     Pre-execution scanner для LLM-сгенерированного C# кода.
///     Срабатывает ДО старта процесса (fail-fast).
///     Default-deny: блокирует опасные API по умолчанию, escape hatch через CustomAllowedNamespaces.
///     Reference: <c>references/code-execution-sandbox.md</c>.
/// </summary>
public static class DangerousCodeScanner
{
    private static readonly Regex[] DefaultBlocked =
    {
        // Filesystem destruction
        new(@"\bFile\.Delete\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bDirectory\.Delete\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"rm\s+-rf", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bRemove-Item\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Format-Volume", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Process / shell escape
        new(@"\bProcess\.Start\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bbash\s+-c\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bsh\s+-c\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"cmd\.exe\s*/c", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bInvoke-Expression\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\beval\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bexec\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Network exfiltration (default deny)
        new(@"\bnew\s+HttpClient\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bnew\s+WebClient\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bTcpClient\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bUdpClient\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bSocket\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bDns\.(GetHostEntry|GetHostAddresses)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Reflection / dynamic code
        new(@"\bAssembly\.Load(File|FromStream|From)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bActivator\.CreateInstance.*Assembly", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bReflection\.Assembly\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Native interop
        new(@"\bDllImport\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bMarshal\.GetDelegateForFunctionPointer\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Persistence
        new(@"\bRegistry\.(CurrentUser|LocalMachine|ClassesRoot)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bSCHTASKS\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bcrontab\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    public sealed record ScanResult(bool IsAllowed, IReadOnlyList<string> BlockedReasons, IReadOnlyList<int> LineNumbers)
    {
        public static ScanResult Allow { get; } = new(true, Array.Empty<string>(), Array.Empty<int>());
        public static ScanResult Deny(string reason, int line) =>
            new(false, new[] { reason }, new[] { line });
    }

    /// <summary>
    ///     Сканировать код построчно. Возвращает на первом нарушении (fail-fast).
    ///     Если в CustomAllowedNamespaces есть совпадение со строкой — она пропускается
    ///     (escape hatch для легитимных сценариев).
    /// </summary>
    public static ScanResult Scan(string code, SandboxOptions opts)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(opts);
        opts.Validate();

        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsLineExplicitlyAllowed(lines[i], opts.CustomAllowedNamespaces))
            {
                continue;
            }

            foreach (var pattern in DefaultBlocked)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    return ScanResult.Deny($"`{pattern}` matched at line {i + 1}", i + 1);
                }
            }

            if (opts.CustomBlockedPatterns is { Length: > 0 })
            {
                foreach (var raw in opts.CustomBlockedPatterns)
                {
                    // Caller must Regex.Escape literal strings (защита от backtracking DoS).
                    var custom = new Regex(
                        raw,
                        RegexOptions.Compiled | RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(50));
                    var m = custom.Match(lines[i]);
                    if (m.Success)
                    {
                        return ScanResult.Deny($"custom rule `{raw}` matched at line {i + 1}", i + 1);
                    }
                }
            }
        }
        return ScanResult.Allow;
    }

    private static bool IsLineExplicitlyAllowed(string line, string[]? allowed)
    {
        if (allowed is null || allowed.Length == 0) return false;
        foreach (var ns in allowed)
        {
            // Token-based match: берём короткий токен (последний сегмент namespace, например "HttpClient")
            // и проверяем его вхождение в строке. Это позволяет легитимно использовать
            // System.Net.Http.HttpClient без написания полного namespace.
            var token = ns;
            var lastDot = ns.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < ns.Length - 1)
            {
                token = ns[(lastDot + 1)..];
            }

            // Match: строка содержит полный namespace ИЛИ короткий токен (word boundary).
            if (line.Contains(ns, StringComparison.Ordinal) ||
                ContainsToken(line, token))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsToken(string line, string token)
    {
        // Простая проверка: токен окружён не-identifier символами или границами строки.
        int idx = 0;
        while ((idx = line.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            bool leftOk = idx == 0 || !IsIdentifierChar(line[idx - 1]);
            bool rightOk = idx + token.Length >= line.Length || !IsIdentifierChar(line[idx + token.Length]);
            if (leftOk && rightOk) return true;
            idx += token.Length;
        }
        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
