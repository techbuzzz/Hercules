// ============================================================================
//  OBSOLETE — task_081: removed in favor of the framework rate limiter
//  (Microsoft.AspNetCore.RateLimiting + System.Threading.RateLimiting).
//
//  Use `RateLimitPolicies.Chat` and `RateLimitPolicies.Expensive` from
//  `Hercules.WebApi.RateLimitPolicies`, registered in `Program.cs`,
//  and apply them per-endpoint via `.RequireRateLimiting(...)`.
//
//  This file is intentionally a no-op stub kept only to preserve
//  compilation history. The previous custom sliding-window logic was
//  unbounded (no eviction of idle IPs) and is replaced by the
//  partitioned `FixedWindowRateLimiter`.
// ============================================================================
namespace Hercules.WebApi.Auth;

/// <summary>
///     Legacy marker — never instantiated. See file header for replacement.
/// </summary>
internal static class _RateLimitMiddlewareObsolete
{
    // Empty by design.
}
