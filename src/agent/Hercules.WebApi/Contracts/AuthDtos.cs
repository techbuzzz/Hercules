namespace Hercules.WebApi.Contracts;

/// <summary>
/// One API key as exposed to Studio. The raw key is **never** included — a key the
/// browser cannot read is one the browser cannot leak. Keys are addressed by
/// <see cref="Fingerprint"/>, a non-reversible id, so Studio can edit and delete a key
/// it has never seen the value of.
/// </summary>
public sealed class ApiKeySummaryDto
{
    /// <summary>Non-reversible identifier used to address this key in the write endpoints.</summary>
    public required string Fingerprint { get; init; }

    /// <summary><c>contribute</c> or <c>system</c>.</summary>
    public required string Role { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Display-only hint about the key's shape (e.g. <c>hc_sys_…</c>). Derived from the
    /// prefix before the random token, never from the token itself.
    /// </summary>
    public required string Label { get; init; }
}

public sealed class ApiKeysListResponseDto
{
    public required int Count { get; init; }
    public required int ContributeCount { get; init; }
    public required int SystemCount { get; init; }
    public required IReadOnlyList<ApiKeySummaryDto> Keys { get; init; }

    /// <summary>Fingerprint of the key backing the calling session, if any.</summary>
    public string? CurrentFingerprint { get; init; }
}

public sealed class CreateApiKeyRequestDto
{
    public required string Role { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Optional operator-supplied key. When omitted the agent generates one and returns
    /// it **once** in <see cref="CreatedApiKeyResponseDto.Key"/> — it is not retrievable afterwards.
    /// </summary>
    public string? Key { get; init; }
}

/// <summary>
/// A newly created key. <see cref="Key"/> is the only time the value is ever returned —
/// it is not stored in the browser and cannot be re-read through any endpoint.
/// </summary>
public sealed class CreatedApiKeyResponseDto
{
    public required ApiKeySummaryDto Key { get; init; }

    /// <summary>Plaintext key, present only when the agent generated it.</summary>
    public string? GeneratedKey { get; init; }
}

public sealed class UpdateApiKeyRequestDto
{
    /// <summary>Optional new role; null leaves the current role untouched.</summary>
    public string? Role { get; init; }

    /// <summary>Optional new description; null leaves the current description untouched.</summary>
    public string? Description { get; init; }
}

public sealed class ApiKeyMutationResponseDto
{
    public required ApiKeySummaryDto Key { get; init; }
    public required int SystemCount { get; init; }
    public required int RevokedSessions { get; init; }
}

public sealed class ApiKeyDeleteResponseDto
{
    public required string Fingerprint { get; init; }
    public required int SystemCount { get; init; }
    public required int RevokedSessions { get; init; }
}