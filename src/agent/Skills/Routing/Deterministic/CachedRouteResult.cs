namespace Hercules.Skills.Routing.Deterministic;

/// <summary>
///     Wrapper for caching DeterministicRouteResult (struct) in ICacheService.
///     Since ICacheService requires class types, we box the result in this class.
/// </summary>
public sealed class CachedRouteResult
{
    public DeterministicRouteResult Result { get; }

    public CachedRouteResult(DeterministicRouteResult result) => Result = result;

    public static CachedRouteResult FromResult(DeterministicRouteResult result) => new(result);
}
