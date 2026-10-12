using System.Text.RegularExpressions;

namespace Hercules.CodeExecution;

/// <summary>
///     Whitelist scanner for SkillSdk file-based code.
///     Complements DangerousCodeScanner (blacklist) with an explicit allow-list of namespaces.
/// </summary>
public static class SkillSdkWhitelistScanner
{
    private static readonly Regex[] DangerousPatterns =
    {
        new(@"\bFile\.Delete\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bDirectory\.Delete\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bProcess\.Start\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bActivator\.CreateInstance\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bAssembly\.Load\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bReflection\.Assembly\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bDllImport\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bunsafe\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bfixed\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bMarshal\.GetDelegateForFunctionPointer\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bRegistry\.(CurrentUser|LocalMachine|ClassesRoot)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    private static readonly string[] AllowedNamespaces =
    {
        "Hercules.SkillSdk",
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Net.Http",
        "System.Net",
        "System.IO"
    };

    private static readonly string[] ForbiddenNamespaces =
    {
        "System.Reflection",
        "System.Diagnostics",
        "System.Runtime.InteropServices",
        "System.Security",
        "Microsoft.Win32"
    };

    public static DangerousCodeScanner.ScanResult Scan(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip comments
            if (IsCommentOrWhitespace(line))
            {
                continue;
            }

            foreach (var pattern in DangerousPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    return DangerousCodeScanner.ScanResult.Deny($"dangerous pattern `{pattern}` matched at line {i + 1}", i + 1);
                }
            }

            if (HasForbiddenNamespace(line))
            {
                return DangerousCodeScanner.ScanResult.Deny($"forbidden namespace at line {i + 1}", i + 1);
            }
        }

        return DangerousCodeScanner.ScanResult.Allow;
    }

    private static bool IsCommentOrWhitespace(string line)
    {
        var trimmed = line.TrimStart();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("/*");
    }

    private static bool HasForbiddenNamespace(string line)
    {
        // using directives only
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("using ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var ns in ForbiddenNamespaces)
        {
            if (line.Contains(ns, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsSkillSdkReference(string code)
    {
        return code.Contains("Hercules.SkillSdk", StringComparison.Ordinal);
    }
}
