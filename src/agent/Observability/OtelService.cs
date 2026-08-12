using System.Diagnostics;
using Hercules.Config;

namespace Hercules.Observability;

/// <summary>
///     Default OTel service implementation wrapping the static ActivitySource.
///     Graceful no-op when OpenTelemetry is disabled via OtelConfig.Enabled = false.
///     Secrets are redacted from telemetry when SecretsConfig.RedactInTelemetry = true (task_015).
/// </summary>
public sealed class OtelService : IOtelService
{
    public bool IsEnabled { get; }

    private readonly SecretsConfig? _secretsConfig;
    private readonly ISecretMaskingService? _masking;

    public OtelService(OtelConfig config)
    {
        IsEnabled = config.Enabled;
    }

    /// <summary>
    ///     Constructor with secret masking support (task_015).
    /// </summary>
    public OtelService(OtelConfig config, SecretsConfig? secretsConfig, ISecretMaskingService? masking)
    {
        IsEnabled = config.Enabled;
        _secretsConfig = secretsConfig;
        _masking = masking;
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
        if (activity is null) return;
        var safeValue = _secretsConfig?.RedactInTelemetry == true && _masking is not null
            ? _masking.MaskSecrets(value)
            : value;
        activity.SetTag(key, safeValue);
    }

    public void SetTags(Activity? activity, params KeyValuePair<string, object?>[] tags)
    {
        if (activity is null) return;
        foreach (var tag in tags)
        {
            var safeValue = tag.Value is string strVal && _secretsConfig?.RedactInTelemetry == true && _masking is not null
                ? _masking.MaskSecrets(strVal)
                : tag.Value;
            activity.SetTag(tag.Key, safeValue);
        }
    }

    public void AddEvent(Activity? activity, string name, params KeyValuePair<string, object?>[] tags)
    {
        if (activity is null) return;

        // Redact secrets from event tags if configured
        KeyValuePair<string, object?>[] safeTags = tags.Length > 0 &&
            _secretsConfig?.RedactInTelemetry == true && _masking is not null
            ? tags.Select(t => t.Value is string sv
                ? new KeyValuePair<string, object?>(t.Key, _masking.MaskSecrets(sv))
                : t).ToArray()
            : tags;

        if (safeTags.Length == 0)
        {
            activity.AddEvent(new ActivityEvent(name));
        }
        else
        {
            var tagCollection = new ActivityTagsCollection(safeTags);
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
