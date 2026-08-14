using System.Text.RegularExpressions;

namespace Hercules.Skills;

/// <summary>
///     Результат валидации манифеста.
/// </summary>
public sealed class SkillManifestValidationResult
{
    /// <summary>Валиден ли манифест.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Список ошибок (пустой = валиден).</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Список предупреждений (non-blocking).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Список имён недоступных инструментов (RequiredTools ⊄ зарегистрированных).</summary>
    public List<string> MissingTools { get; set; } = new();

    /// <summary>
    ///     Совместим ли навык с текущей версией Hercules.
    ///     Null = совместимость не проверялась (NotSupported в KnownTools).
    /// </summary>
    public bool? IsCompatible { get; set; }
}

/// <summary>
///     Валидация skill manifest: schema drift, Hercules-версия, RequiredTools.
/// </summary>
public sealed class SkillManifestValidator
{
    private static readonly Regex SemverRegex = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled);

    /// <summary>Текущая версия Hercules (semver). Задаётся через конструктор или из AssemblyVersion.</summary>
    public string CurrentHerculesVersion { get; }

    /// <summary>Множество зарегистрированных имён инструментов (KnownTools.Keys).</summary>
    public IReadOnlySet<string> KnownToolNames { get; }

    /// <summary>
    ///     Разрешённые уровни риска. Навык с RiskLevel вне этого списка получает ошибку.
    ///     Null = без ограничений.
    /// </summary>
    public IReadOnlyList<SkillRiskLevel>? AllowedRiskLevels { get; }

    /// <summary>
    ///     Создать валидатор.
    /// </summary>
    /// <param name="currentHerculesVersion">Версия Hercules в semver (например "1.0.0").</param>
    /// <param name="knownToolNames">Множество зарегистрированных имён инструментов. Если null — проверка RequiredTools пропускается.</param>
    /// <param name="allowedRiskLevels">Разрешённые уровни риска. Если null — без проверки.</param>
    public SkillManifestValidator(
        string currentHerculesVersion,
        IReadOnlySet<string>? knownToolNames = null,
        IReadOnlyList<SkillRiskLevel>? allowedRiskLevels = null)
    {
        CurrentHerculesVersion = currentHerculesVersion;
        KnownToolNames = knownToolNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AllowedRiskLevels = allowedRiskLevels;
    }

    /// <summary>
    ///     Валидировать манифест навыка.
    /// </summary>
    /// <param name="manifest">Манифест для валидации (nullable — обрабатывается корректно).</param>
    /// <returns>Результат валидации с ошибками и предупреждениями.</returns>
    public SkillManifestValidationResult Validate(SkillManifest? manifest)
    {
        var result = new SkillManifestValidationResult();

        if (manifest is null)
        {
            result.Errors.Add("Манифест навыка отсутствует (null).");
            return result;
        }

        // 1. SchemaVersion
        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            result.Errors.Add("SchemaVersion не указан.");
        }
        else if (!IsValidSemver(manifest.SchemaVersion))
        {
            result.Errors.Add($"SchemaVersion не соответствует semver: \"{manifest.SchemaVersion}\".");
        }

        // 2. MinHerculesVersion / MaxHerculesVersion
        if (!string.IsNullOrWhiteSpace(manifest.MinHerculesVersion) &&
            !IsValidSemver(manifest.MinHerculesVersion))
        {
            result.Errors.Add($"MinHerculesVersion не соответствует semver: \"{manifest.MinHerculesVersion}\".");
        }

        if (!string.IsNullOrWhiteSpace(manifest.MaxHerculesVersion) &&
            !IsValidSemver(manifest.MaxHerculesVersion))
        {
            result.Errors.Add($"MaxHerculesVersion не соответствует semver: \"{manifest.MaxHerculesVersion}\".");
        }

        // 3. Hercules version compatibility
        if (!string.IsNullOrWhiteSpace(manifest.MinHerculesVersion) &&
            IsValidSemver(manifest.MinHerculesVersion))
        {
            if (CompareSemver(CurrentHerculesVersion, manifest.MinHerculesVersion) < 0)
            {
                result.Errors.Add(
                    $"Hercules {CurrentHerculesVersion} старше MinHerculesVersion ({manifest.MinHerculesVersion}). Навык несовместим.");
                result.IsCompatible = false;
            }
        }

        if (result.IsCompatible is null && !string.IsNullOrWhiteSpace(manifest.MaxHerculesVersion) &&
            IsValidSemver(manifest.MaxHerculesVersion))
        {
            if (CompareSemver(CurrentHerculesVersion, manifest.MaxHerculesVersion) > 0)
            {
                result.Errors.Add(
                    $"Hercules {CurrentHerculesVersion} новее MaxHerculesVersion ({manifest.MaxHerculesVersion}). Навык может быть несовместим.");
                result.IsCompatible = false;
            }
            else
            {
                result.IsCompatible = true;
            }
        }
        else if (result.IsCompatible is null)
        {
            result.IsCompatible = true;
        }

        // 4. InputSchemaVersion / OutputSchemaVersion
        if (!string.IsNullOrWhiteSpace(manifest.InputSchemaVersion) &&
            !IsValidSemver(manifest.InputSchemaVersion))
        {
            result.Errors.Add($"InputSchemaVersion не соответствует semver: \"{manifest.InputSchemaVersion}\".");
        }

        if (!string.IsNullOrWhiteSpace(manifest.OutputSchemaVersion) &&
            !IsValidSemver(manifest.OutputSchemaVersion))
        {
            result.Errors.Add($"OutputSchemaVersion не соответствует semver: \"{manifest.OutputSchemaVersion}\".");
        }

        // 5. RequiredTools — проверка против зарегистрированных
        if (manifest.RequiredTools.Count > 0 && KnownToolNames.Count > 0)
        {
            foreach (var tool in manifest.RequiredTools)
            {
                if (!KnownToolNames.Contains(tool))
                {
                    result.MissingTools.Add(tool);
                    result.Warnings.Add($"Инструмент \"{tool}\" не зарегистрирован в Hercules.");
                }
            }
        }

        // 6. RiskLevel
        if (AllowedRiskLevels is { Count: > 0 })
        {
            if (!AllowedRiskLevels.Contains(manifest.RiskLevel))
            {
                result.Errors.Add(
                    $"RiskLevel \"{manifest.RiskLevel}\" не в списке разрешённых: [{string.Join(", ", AllowedRiskLevels)}].");
            }
        }

        // 7. Budget sanity
        if (manifest.Budget is not null)
        {
            if (manifest.Budget.MaxTokensPerCall < 0)
            {
                result.Errors.Add("Budget.MaxTokensPerCall не может быть отрицательным.");
            }

            if (manifest.Budget.MaxCallsPerMinute < 0)
            {
                result.Errors.Add("Budget.MaxCallsPerMinute не может быть отрицательным.");
            }

            if (manifest.Budget.MaxCostPerCallUsd < 0)
            {
                result.Errors.Add("Budget.MaxCostPerCallUsd не может быть отрицательным.");
            }
        }

        return result;
    }

    /// <summary>
    ///     Проверить, совместим ли навык с текущей версией Hercules (quick check).
    /// </summary>
    public bool IsCompatible(SkillManifest? manifest)
    {
        if (manifest is null) return false;

        if (!string.IsNullOrWhiteSpace(manifest.MinHerculesVersion) &&
            IsValidSemver(manifest.MinHerculesVersion) &&
            CompareSemver(CurrentHerculesVersion, manifest.MinHerculesVersion) < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MaxHerculesVersion) &&
            IsValidSemver(manifest.MaxHerculesVersion) &&
            CompareSemver(CurrentHerculesVersion, manifest.MaxHerculesVersion) > 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidSemver(string version)
    {
        return SemverRegex.IsMatch(version);
    }

    /// <summary>
    ///     Сравнить две semver-строки.
    ///     Returns: negative if v1 &lt; v2, 0 if v1 == v2, positive if v1 &gt; v2.
    /// </summary>
    private static int CompareSemver(string v1, string v2)
    {
        // Normalize to 0.0.0 for comparison
        var parts1 = ParseSemver(v1);
        var parts2 = ParseSemver(v2);

        for (int i = 0; i < 3; i++)
        {
            if (parts1[i] < parts2[i]) return -1;
            if (parts1[i] > parts2[i]) return 1;
        }

        return 0;
    }

    private static int[] ParseSemver(string v)
    {
        var parts = v.Split('-')[0].Split('.');
        return new[]
        {
            parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0
        };
    }
}
