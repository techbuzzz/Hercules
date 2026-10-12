namespace Hercules.WebApi.Contracts;

/// <summary>
/// Response shape for <c>GET /api/quotas</c> (Stage 6.5).
/// <para>
/// <c>IQuotaService</c> is read-only — limits live in <c>AppConfig.Quotas</c>. An
/// "editor" therefore means writing the <c>quotas</c> section via
/// <c>PATCH /api/config</c>, which the Studio config view already does. This DTO
/// exists so the live status can be generated rather than hand-written.
/// </para>
/// </summary>
public sealed class QuotaStatusDto
{
    public required string Type { get; init; }
    public required long Limit { get; init; }
    public required long Current { get; init; }
    public required long Remaining { get; init; }
    public required bool IsExceeded { get; init; }
    public required bool IsHardCap { get; init; }
    public required double UsagePercent { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
}

public sealed class QuotaCountersDto
{
    public required long TokensUsedToday { get; init; }
    public required long MessagesUsedToday { get; init; }
    public required long RequestsUsedToday { get; init; }
    public required long StorageUsedMb { get; init; }
    public required long CostUsedTodayCents { get; init; }
    public required long ActiveConcurrentRequests { get; init; }
    public required long ActiveSkillExecutions { get; init; }
    public DateTimeOffset? LastResetDate { get; init; }
}

public sealed class QuotaStatusResponseDto
{
    public required string Scope { get; init; }
    public required string ScopeId { get; init; }
    public required QuotaCountersDto Counters { get; init; }
    public required IReadOnlyList<QuotaStatusDto> Limits { get; init; }
}