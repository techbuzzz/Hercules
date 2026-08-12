namespace Hercules.Cache;

/// <summary>
///     Sensitivity level for cached values.
///     Controls which invalidation scope and retention rules apply.
/// </summary>
public enum SensitivityLevel
{
    /// <summary>Public data with no privacy risk. Longest TTL allowed.</summary>
    Public,

    /// <summary>Internal business data. Medium TTL, scoped invalidation.</summary>
    Internal,

    /// <summary>Sensitive user or session data. Short TTL, eager invalidation.</summary>
    Sensitive,

    /// <summary>Restricted data (credentials, secrets, PII). Minimal caching, immediate invalidation available.</summary>
    Restricted,
}
