using System.Text.RegularExpressions;

namespace Hercules.Skills.Marketplace;

/// <summary>
///     Результат разрешения зависимостей.
/// </summary>
public sealed record DependencyResolutionResult(
    bool Success,
    List<string> SkillsToInstall,
    List<string> MissingRequired,
    List<string> Cycles,
    string? Error);

/// <summary>
///     Информация о зависимости для отображения.
/// </summary>
public sealed record DependencyInfo(
    string SkillId,
    string? VersionConstraint,
    bool IsRequired,
    bool IsSatisfied,
    string? InstalledVersion,
    string? MarketplaceVersion);

/// <summary>
///     Результат проверки пакета (hash + signature).
/// </summary>
public sealed record PackageVerificationResult(
    bool IsValid,
    string? Hash,
    bool HashValid,
    bool SignatureValid,
    string? Error);

/// <summary>
///     Сервис разрешения transitive dependencies между навыками в маркетплейсе.
///     Iterative post-order DFS с enter/exit markers.
/// </summary>
public sealed class DependencyResolver
{
    private const string EnterPrefix = "enter:";
    private const string ExitPrefix = "exit:";

    /// <summary>
    ///     Разрешить все зависимости для указанного навыка.
    ///     Возвращает топологически отсортированный список ID для установки
    ///     (зависимости первыми, root НЕ включается).
    ///     getManifest возвращает null для "manifest not found" или пустой массив для "no deps".
    /// </summary>
    public async Task<DependencyResolutionResult> ResolveAsync(
        string rootSkillId,
        Func<string, Task<SkillDependency[]?>> getManifest,
        Func<string, Task<(int Version, bool Exists)>> getInstalledVersion,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();
        var installOrder = new List<string>();
        var missingRequired = new List<string>();
        var cycles = new List<string>();

        // Stack entries: "enter:{skillId}" (first visit) or "exit:{skillId}" (post-order)
        var stack = new Stack<string>();
        stack.Push(EnterPrefix + rootSkillId);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = stack.Pop();

            if (!entry.StartsWith(EnterPrefix) && !entry.StartsWith(ExitPrefix))
            {
                continue;
            }

            var isEnter = entry.StartsWith(EnterPrefix);
            var skillId = isEnter
                ? entry.Substring(EnterPrefix.Length)
                : entry.Substring(ExitPrefix.Length);

            if (isEnter)
            {
                // === ENTER phase ===
                if (visited.Contains(skillId))
                {
                    continue;
                }

                if (onStack.Contains(skillId))
                {
                    // Back edge — cycle
                    if (!cycles.Contains(skillId))
                    {
                        cycles.Add(skillId);
                    }
                    continue;
                }

                onStack.Add(skillId);

                SkillDependency[]? deps;
                try
                {
                    deps = await getManifest(skillId).WaitAsync(cancellationToken);
                }
                catch
                {
                    deps = null;
                }

                // null = manifest not found (treat as no deps but log issue)
                if (deps == null || deps.Length == 0)
                {
                    // Leaf: done immediately
                    visited.Add(skillId);
                    onStack.Remove(skillId);
                    if (skillId != rootSkillId)
                    {
                        installOrder.Add(skillId);
                    }
                    continue;
                }

                // Push exit marker first, then deps in reverse (so first dep is on top)
                stack.Push(ExitPrefix + skillId);
                for (var i = deps.Length - 1; i >= 0; i--)
                {
                    var dep = deps[i];

                    // Check installed version
                    var installed = await getInstalledVersion(dep.Id).WaitAsync(cancellationToken);
                    if (!installed.Exists)
                    {
                        if (dep.IsRequired)
                        {
                            missingRequired.Add(dep.Id);
                        }
                    }
                    else if (!VersionMatchesConstraint(installed.Version, dep.VersionConstraint))
                    {
                        missingRequired.Add($"{dep.Id} (installed v{installed.Version} doesn't match {dep.VersionConstraint})");
                    }

                    // Only push if not already visited
                    if (!visited.Contains(dep.Id))
                    {
                        stack.Push(EnterPrefix + dep.Id);
                    }
                }
            }
            else
            {
                // === EXIT phase ===
                visited.Add(skillId);
                onStack.Remove(skillId);
                if (skillId != rootSkillId)
                {
                    installOrder.Add(skillId);
                }
            }
        }

        if (cycles.Count > 0)
        {
            return new DependencyResolutionResult(
                Success: false,
                SkillsToInstall: installOrder,
                MissingRequired: missingRequired.Distinct().ToList(),
                Cycles: cycles.Distinct().ToList(),
                Error: $"Cycle detected: {string.Join(", ", cycles.Distinct())}");
        }

        return new DependencyResolutionResult(
            Success: true,
            SkillsToInstall: installOrder,
            MissingRequired: missingRequired.Distinct().ToList(),
            Cycles: new List<string>(),
            Error: null);
    }

    /// <summary>
    ///     Проверить совместимость версии с semver constraint.
    ///     Поддерживает: ^x.y.z (compatible), ~x.y.z (patch), >=, <=, >, &lt;, exact.
    ///     Версия кодируется как int = major*10000 + minor*100 + patch.
    /// </summary>
    public static bool VersionMatchesConstraint(int version, string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
        {
            return true;
        }

        var normalized = constraint.Trim();

        if (normalized.StartsWith("^"))
        {
            var rest = normalized[1..];
            var parts = rest.Split('.');
            if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            {
                var lower = major * 10000;
                var upper = (major + 1) * 10000;
                return version >= lower && version < upper;
            }
        }

        if (normalized.StartsWith("~"))
        {
            var rest = normalized[1..];
            var parts = rest.Split('.');
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], out var major) &&
                int.TryParse(parts[1], out var minor))
            {
                var lower = major * 10000 + minor * 100;
                var upper = major * 10000 + (minor + 1) * 100;
                return version >= lower && version < upper;
            }
        }

        var match = Regex.Match(normalized, @"^([><=]+)\s*(\d+)$");
        if (match.Success)
        {
            var op = match.Groups[1].Value;
            if (int.TryParse(match.Groups[2].Value, out var targetVersion))
            {
                return op switch
                {
                    ">=" => version >= targetVersion,
                    "<=" => version <= targetVersion,
                    ">" => version > targetVersion,
                    "<" => version < targetVersion,
                    "=" or "==" => version == targetVersion,
                    _ => true
                };
            }
        }

        var exact = normalized.Split('.');
        if (exact.Length >= 1 && int.TryParse(exact[0], out var exactMajor))
        {
            return version == exactMajor * 10000;
        }

        return true;
    }
}
