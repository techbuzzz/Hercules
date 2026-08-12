namespace Hercules.Tools.Policy;

/// <summary>
///     Набор выданных tool'у permissions.
///     Проверяет, есть ли у агента все required permissions для выполнения tool.
/// </summary>
public sealed class ToolPermissionSet
{
    private readonly ToolPermission _granted;

    public ToolPermissionSet(ToolPermission granted = ToolPermission.None)
    {
        _granted = granted;
    }

    /// <summary>Есть ли у агента данное разрешение.</summary>
    public bool Has(ToolPermission permission) => (_granted & permission) == permission;

    /// <summary>Есть ли все required permissions.</summary>
    public bool HasAll(ToolPermission required)
    {
        if (required == ToolPermission.None) return true;
        return (_granted & required) == required;
    }

    /// <summary>Каких permissions не хватает.</summary>
    public ToolPermission Missing(ToolPermission required)
    {
        return required & ~_granted;
    }

    /// <summary>Проверить конкретное разрешение.</summary>
    public ToolPermissionSet With(ToolPermission permission) => new(_granted | permission);

    /// <summary>Создать набор с default permissions для Hercules.</summary>
    public static ToolPermissionSet Default => new(
        ToolPermission.Read |
        ToolPermission.Write |
        ToolPermission.Network |
        ToolPermission.Memory);

    /// <summary>Создать restricted набор (только read).</summary>
    public static ToolPermissionSet ReadOnly => new(ToolPermission.Read);
}

/// <summary>
///     Расширения для удобной работы с ToolPermission flags.
/// </summary>
public static class ToolPermissionExtensions
{
    public static string ToHumanReadable(this ToolPermission p)
    {
        if (p == ToolPermission.None) return "none";
        var parts = new List<string>();
        foreach (ToolPermission flag in Enum.GetValues<ToolPermission>())
        {
            if (flag != ToolPermission.None && (p & flag) == flag)
            {
                parts.Add(flag.ToString().ToLowerInvariant());
            }
        }
        return string.Join("|", parts);
    }

    public static ToolPermission ParseFromString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ToolPermission.None;
        var result = ToolPermission.None;
        foreach (var part in s.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<ToolPermission>(part.Trim(), true, out var p))
            {
                result |= p;
            }
        }
        return result;
    }
}
