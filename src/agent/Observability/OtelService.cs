using System.Diagnostics;
using Hercules.Config;

namespace Hercules.Observability;

/// <summary>
///     Default OTel service implementation wrapping the static ActivitySource.
///     Graceful no-op when OpenTelemetry is disabled via OtelConfig.Enabled = false.
/// </summary>
public sealed class OtelService : IOtelService
{
    public bool IsEnabled { get; }

    public OtelService(OtelConfig config)
    {
        IsEnabled = config.Enabled;
    }

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        if (!IsEnabled) return null;
        return OtelSetup.Source.StartActivity(name, kind);
    }

    public Activity? StartActivity(string name, ActivityContext parent, ActivityKind kind = ActivityKind.Internal)
    {
        if (!IsEnabled) return null;
        // .NET overload: StartActivity(string name, ActivityKind kind, ActivityContext parent)
        return OtelSetup.Source.StartActivity(name, kind, parent);
    }

    public void SetTag(Activity? activity, string key, string value)
    {
        activity?.SetTag(key, value);
    }

    public void SetTags(Activity? activity, params KeyValuePair<string, object?>[] tags)
    {
        if (activity is null) return;
        foreach (var tag in tags)
        {
            activity.SetTag(tag.Key, tag.Value);
        }
    }

    public void AddEvent(Activity? activity, string name, params KeyValuePair<string, object?>[] tags)
    {
        if (activity is null) return;
        if (tags.Length == 0)
        {
            activity.AddEvent(new ActivityEvent(name));
        }
        else
        {
            var tagCollection = new ActivityTagsCollection(tags);
            activity.AddEvent(new ActivityEvent(name, tags: tagCollection));
        }
    }

    public void SetErrorStatus(Activity? activity, string description)
    {
        if (activity is null) return;
        activity.SetStatus(ActivityStatusCode.Error, description);
    }

    public void StopActivity(Activity? activity, ActivityStatusCode status = ActivityStatusCode.Ok)
    {
        if (activity is null) return;
        if (status != ActivityStatusCode.Ok)
        {
            activity.SetStatus(status);
        }
        activity.Stop();
    }
}
