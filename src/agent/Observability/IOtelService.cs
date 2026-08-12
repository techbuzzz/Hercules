using System.Diagnostics;

namespace Hercules.Observability;

/// <summary>
///     Abstraction over OpenTelemetry Activity.
///     Allows instrumented services to be testable and graceful when OTel is disabled.
/// </summary>
public interface IOtelService
{
    /// <summary>Whether OpenTelemetry is enabled (enables fast-path skip in hot paths).</summary>
    bool IsEnabled { get; }

    /// <summary>Start a new Activity (span). Returns null if disabled — callers must guard.</summary>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

    /// <summary>Start a child activity from a parent ActivityContext.</summary>
    Activity? StartActivity(string name, ActivityContext parent, ActivityKind kind = ActivityKind.Internal);

    /// <summary>Add a key-value tag to an Activity.</summary>
    void SetTag(Activity? activity, string key, string value);

    /// <summary>Add multiple tags to an Activity.</summary>
    void SetTags(Activity? activity, params KeyValuePair<string, object?>[] tags);

    /// <summary>Record an event on an Activity (e.g. "exception", "cache_hit").</summary>
    void AddEvent(Activity? activity, string name, params KeyValuePair<string, object?>[] tags);

    /// <summary>Set the status of an Activity to Error.</summary>
    void SetErrorStatus(Activity? activity, string description);

    /// <summary>Stop an Activity and optionally set its end time.</summary>
    void StopActivity(Activity? activity, ActivityStatusCode status = ActivityStatusCode.Ok);
}
