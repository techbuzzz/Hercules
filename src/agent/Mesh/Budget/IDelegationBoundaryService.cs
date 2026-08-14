using Hercules.Mesh;
using Hercules.Mesh.Schema;

namespace Hercules.Budget;

/// <summary>
///     Service for enforcing delegation boundaries: hop count, fan-out width,
///     cumulative tool calls, cost, and wall-clock time limits on inter-agent delegation chains.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_048.
/// </summary>
public interface IDelegationBoundaryService
{
    /// <summary>
    ///     Check outgoing delegation boundaries before sending a request to a peer.
    ///     Returns violations if any boundary is exceeded.
    /// </summary>
    DelegationBoundaryCheckResult CheckOutgoing(
        string agentId,
        int fanOutWidth,
        decimal estimatedCostUsd,
        int estimatedToolCalls,
        long estimatedWallClockMs,
        AuthContext? auth,
        string? rootRequestId = null);

    /// <summary>
    ///     Check incoming delegation boundaries when receiving a request.
    ///     Validates hop count, cost, tool calls, and wall-clock time against config.
    /// </summary>
    DelegationBoundaryCheckResult CheckIncoming(AuthContext? auth);

    /// <summary>
    ///     Record metrics after a delegation hop completes.
    ///     Updates the chain context with actual values.
    /// </summary>
    void RecordHopCompletion(
        string agentId,
        int actualToolCalls,
        decimal actualCostUsd,
        long actualWallClockMs,
        string? rootRequestId = null);

    /// <summary>
    ///     Create the auth context for the next hop (incremented depth + root request ID propagation).
    /// </summary>
    AuthContext CreateNextHopAuthContext(AuthContext? auth, string? rootRequestId = null);

    /// <summary>
    ///     Build a rejection response for a boundary violation.
    /// </summary>
    IntentResponse CreateRejectionResponse(
        string requestId,
        string agentId,
        DelegationBoundaryCheckResult result,
        string? traceId = null);

    /// <summary>
    ///     Get current boundary status for a chain.
    /// </summary>
    DelegationBoundaryContext? GetChainContext(string rootRequestId);
}
