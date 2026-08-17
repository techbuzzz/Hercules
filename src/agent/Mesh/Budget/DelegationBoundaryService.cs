using System.Collections.Concurrent;
using Hercules.Config;
using Hercules.Mesh;
using Hercules.Mesh.Schema;
using Microsoft.Extensions.Logging;

namespace Hercules.Budget;

/// <summary>
///     Implementation of <see cref="IDelegationBoundaryService"/>.
///     Tracks delegation chain metrics in-memory and enforces configurable boundaries.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_048.
/// </summary>
public sealed class DelegationBoundaryService : IDelegationBoundaryService
{
    private readonly DelegationBoundaryConfig _config;
    private readonly ILogger<DelegationBoundaryService> _logger;

    // Keyed by root request ID — one context per delegation chain
    private readonly ConcurrentDictionary<string, DelegationBoundaryContext> _chainContexts = new();

    public DelegationBoundaryService(
        DelegationBoundaryConfig config,
        ILogger<DelegationBoundaryService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DelegationBoundaryCheckResult CheckOutgoing(
        string agentId,
        int fanOutWidth,
        decimal estimatedCostUsd,
        int estimatedToolCalls,
        long estimatedWallClockMs,
        AuthContext? auth,
        string? rootRequestId = null)
    {
        if (!_config.Enabled)
        {
            return DelegationBoundaryCheckResultFactory.Ok();
        }

        var violations = new List<DelegationBoundaryViolation>();
        var currentDepth = auth?.DelegationDepth ?? 0;

        // 1. Hop count check (depth after this hop)
        int nextHop = currentDepth + 1;
        if (_config.MaxHopCount > 0 && nextHop > _config.MaxHopCount)
        {
            violations.Add(new DelegationBoundaryViolation(
                DelegationBoundaryType.HopCount,
                _config.MaxHopCount,
                nextHop,
                $"Hop count {nextHop} exceeds max {_config.MaxHopCount}"));
        }

        // 2. Fan-out width check
        if (_config.MaxFanOutWidth > 0 && fanOutWidth > _config.MaxFanOutWidth)
        {
            violations.Add(new DelegationBoundaryViolation(
                DelegationBoundaryType.FanOutWidth,
                _config.MaxFanOutWidth,
                fanOutWidth,
                $"Fan-out width {fanOutWidth} exceeds max {_config.MaxFanOutWidth}"));
        }

        // 3. Cumulative cost check (projected)
        if (_config.MaxCumulativeCostUsd > 0)
        {
            DelegationBoundaryContext? ctx = GetOrCreateChainContext(rootRequestId);
            decimal projected = ctx.CumulativeCostUsd + estimatedCostUsd;
            if (projected > _config.MaxCumulativeCostUsd)
            {
                violations.Add(new DelegationBoundaryViolation(
                    DelegationBoundaryType.CumulativeCostUsd,
                    (long)_config.MaxCumulativeCostUsd,
                    (long)projected,
                    $"Projected cumulative cost {projected:C} exceeds max {_config.MaxCumulativeCostUsd:C}"));
            }
        }

        // 4. Cumulative tool calls check (projected)
        if (_config.MaxCumulativeToolCalls > 0)
        {
            DelegationBoundaryContext? ctx = GetOrCreateChainContext(rootRequestId);
            int projected = ctx.CumulativeToolCalls + estimatedToolCalls;
            if (projected > _config.MaxCumulativeToolCalls)
            {
                violations.Add(new DelegationBoundaryViolation(
                    DelegationBoundaryType.CumulativeToolCalls,
                    _config.MaxCumulativeToolCalls,
                    projected,
                    $"Projected cumulative tool calls {projected} exceeds max {_config.MaxCumulativeToolCalls}"));
            }
        }

        // 5. Cumulative wall-clock check (projected)
        if (_config.MaxCumulativeWallClockMs > 0)
        {
            DelegationBoundaryContext? ctx = GetOrCreateChainContext(rootRequestId);
            long projected = ctx.CumulativeWallClockMs + estimatedWallClockMs;
            if (projected > _config.MaxCumulativeWallClockMs)
            {
                violations.Add(new DelegationBoundaryViolation(
                    DelegationBoundaryType.CumulativeWallClockMs,
                    _config.MaxCumulativeWallClockMs,
                    projected,
                    $"Projected cumulative wall-clock {projected}ms exceeds max {_config.MaxCumulativeWallClockMs}ms"));
            }
        }

        bool isHard = violations.Count > 0 && IsHardCap(_config.EnforcementMode);

        if (violations.Count > 0)
        {
            _logger.LogWarning(
                "[DelegationBoundary] Outgoing delegation blocked/hard-capped: {Violations}. Agent={AgentId}, Depth={Depth}",
                string.Join("; ", violations.Select(v => v.Message)),
                agentId, nextHop);
        }

        return new DelegationBoundaryCheckResult(
            IsAllowed: !isHard,
            IsHardViolation: isHard,
            Violations: violations);
    }

    /// <inheritdoc />
    public DelegationBoundaryCheckResult CheckIncoming(AuthContext? auth)
    {
        if (!_config.Enabled)
        {
            return DelegationBoundaryCheckResultFactory.Ok();
        }

        var violations = new List<DelegationBoundaryViolation>();
        int depth = auth?.DelegationDepth ?? 0;

        // 1. Hop count check
        if (_config.MaxHopCount > 0 && depth > _config.MaxHopCount)
        {
            violations.Add(new DelegationBoundaryViolation(
                DelegationBoundaryType.HopCount,
                _config.MaxHopCount,
                depth,
                $"Delegation depth {depth} exceeds max {_config.MaxHopCount}"));
        }

        // 2. Cumulative cost check
        if (_config.MaxCumulativeCostUsd > 0 && auth != null)
        {
            string? rootReqId = auth.RootRequestId;
            if (!string.IsNullOrEmpty(rootReqId))
            {
                if (_chainContexts.TryGetValue(rootReqId, out var ctx))
                {
                    if (ctx.CumulativeCostUsd > _config.MaxCumulativeCostUsd)
                    {
                        violations.Add(new DelegationBoundaryViolation(
                            DelegationBoundaryType.CumulativeCostUsd,
                            (long)_config.MaxCumulativeCostUsd,
                            (long)ctx.CumulativeCostUsd,
                            $"Cumulative cost {ctx.CumulativeCostUsd:C} exceeds max {_config.MaxCumulativeCostUsd:C}"));
                    }
                }
            }
        }

        // 3. Cumulative tool calls check
        if (_config.MaxCumulativeToolCalls > 0 && auth?.RootRequestId != null)
        {
            if (_chainContexts.TryGetValue(auth.RootRequestId, out var ctx))
            {
                if (ctx.CumulativeToolCalls > _config.MaxCumulativeToolCalls)
                {
                    violations.Add(new DelegationBoundaryViolation(
                        DelegationBoundaryType.CumulativeToolCalls,
                        _config.MaxCumulativeToolCalls,
                        ctx.CumulativeToolCalls,
                        $"Cumulative tool calls {ctx.CumulativeToolCalls} exceeds max {_config.MaxCumulativeToolCalls}"));
                }
            }
        }

        // 4. Cumulative wall-clock check
        if (_config.MaxCumulativeWallClockMs > 0 && auth?.RootRequestId != null)
        {
            if (_chainContexts.TryGetValue(auth.RootRequestId, out var ctx))
            {
                if (ctx.CumulativeWallClockMs > _config.MaxCumulativeWallClockMs)
                {
                    violations.Add(new DelegationBoundaryViolation(
                        DelegationBoundaryType.CumulativeWallClockMs,
                        _config.MaxCumulativeWallClockMs,
                        ctx.CumulativeWallClockMs,
                        $"Cumulative wall-clock {ctx.CumulativeWallClockMs}ms exceeds max {_config.MaxCumulativeWallClockMs}ms"));
                }
            }
        }

        bool isHard = violations.Count > 0 && IsHardCap(_config.EnforcementMode);

        if (violations.Count > 0)
        {
            _logger.LogWarning(
                "[DelegationBoundary] Incoming delegation {Status}: {Violations}. Depth={Depth}",
                isHard ? "rejected (hard)" : "accepted with warnings (soft)",
                string.Join("; ", violations.Select(v => v.Message)),
                depth);
        }

        return new DelegationBoundaryCheckResult(
            IsAllowed: !isHard,
            IsHardViolation: isHard,
            Violations: violations);
    }

    /// <inheritdoc />
    public void RecordHopCompletion(
        string agentId,
        int actualToolCalls,
        decimal actualCostUsd,
        long actualWallClockMs,
        string? rootRequestId = null)
    {
        if (!_config.Enabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(rootRequestId))
        {
            return;
        }

        var ctx = GetOrCreateChainContext(rootRequestId);
        ctx.RecordHop(agentId, actualToolCalls, actualCostUsd, actualWallClockMs);

        _logger.LogDebug(
            "[DelegationBoundary] Recorded hop completion: Agent={AgentId}, RootRequest={RootRequestId}, Hop={Hop}, ToolCalls={ToolCalls}, Cost={Cost}, WallClock={WallClock}ms",
            agentId, rootRequestId, ctx.HopCount, actualToolCalls, actualCostUsd, actualWallClockMs);

        // [task_087] Opportunistic cleanup: keeps the dictionary bounded without
        // a separate background timer. Cheap O(N) sweep, but only on a hop
        // completion — not on every check. If chains become very chatty we can
        // swap this for a hosted BackgroundService later.
        if (_chainContexts.Count > 64)
        {
            CleanupExpiredChainContexts();
        }
    }

    /// <inheritdoc />
    public AuthContext CreateNextHopAuthContext(AuthContext? auth, string? rootRequestId = null)
    {
        if (auth != null)
        {
            // Increment depth and propagate root request ID
            string rootId = rootRequestId ?? auth.RootRequestId ?? IntentIds.NewRequestId();
            var incremented = auth.WithIncrementedDepth();
            return new AuthContext
            {
                AuthType = incremented.AuthType,
                Token = incremented.Token,
                Claims = incremented.Claims,
                DelegationDepth = incremented.DelegationDepth,
                RootRequestId = rootId,
                Version = incremented.Version
            };
        }

        // No auth context — create a new one with depth 0
        string newRootId = rootRequestId ?? IntentIds.NewRequestId();
        return AuthContext.Bearer("", 0, newRootId);
    }

    /// <inheritdoc />
    public IntentResponse CreateRejectionResponse(
        string requestId,
        string agentId,
        DelegationBoundaryCheckResult result,
        string? traceId = null)
    {
        string reason = result.Violations.Count > 0
            ? $"Delegation boundary violation(s): {string.Join("; ", result.Violations.Select(v => v.Message))}"
            : "Delegation boundary exceeded";

        return IntentResponse.Rejected(requestId, agentId, reason, traceId);
    }

    /// <inheritdoc />
    public DelegationBoundaryContext? GetChainContext(string rootRequestId)
    {
        _chainContexts.TryGetValue(rootRequestId, out var ctx);
        return ctx;
    }

    private DelegationBoundaryContext GetOrCreateChainContext(string? rootRequestId)
    {
        if (string.IsNullOrEmpty(rootRequestId))
        {
            rootRequestId = IntentIds.NewRequestId();
        }

        return _chainContexts.GetOrAdd(rootRequestId, _ => new DelegationBoundaryContext
        {
            RootRequestId = rootRequestId,
            ChainStartUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    ///     [task_087] Evict chain contexts whose <c>UpdatedUtc</c> is older than
    ///     <see cref="DelegationBoundaryConfig.ChainContextTtlSec"/>. Cheap O(N)
    ///     sweep over the dictionary; intended to be called opportunistically
    ///     (e.g. once per N accesses) or explicitly from a host shutdown hook.
    ///     Returns the number of evicted entries.
    /// </summary>
    public int CleanupExpiredChainContexts()
    {
        if (_config.ChainContextTtlSec <= 0)
        {
            return 0; // TTL disabled — keep everything.
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_config.ChainContextTtlSec);
        var stale = _chainContexts
            .Where(kvp => kvp.Value.UpdatedUtc < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        var evicted = 0;
        foreach (var key in stale)
        {
            if (_chainContexts.TryRemove(key, out _))
            {
                evicted++;
            }
        }

        if (evicted > 0)
        {
            _logger.LogDebug(
                "[DelegationBoundary] Evicted {Count} expired chain context(s) older than {TtlSec}s",
                evicted, _config.ChainContextTtlSec);
        }

        return evicted;
    }

    private static bool IsHardCap(string enforcementMode)
    {
        return string.Equals(enforcementMode, "HardCap", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
///     Static factory for <see cref="DelegationBoundaryCheckResult"/>.
/// </summary>
internal static class DelegationBoundaryCheckResultFactory
{
    public static DelegationBoundaryCheckResult Ok() =>
        new(IsAllowed: true, IsHardViolation: false, Violations: Array.Empty<DelegationBoundaryViolation>());
}
