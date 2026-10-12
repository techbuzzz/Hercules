using System.Text.Json;

namespace Hercules.WebApi.Config;

/// <summary>
///     Redacts secret-bearing values from the configuration served by <c>GET /api/config</c>.
///     <para>
///     <c>/api/config</c> returns the whole <see cref="Hercules.Config.AppConfig"/>, and
///     <c>AppConfig</c> carries real secrets — LLM provider API keys, the Postgres
///     session-store connection string (which embeds a password), a Telegram bot token and
///     a signing key. The endpoint is readable by any authenticated session, including
///     <c>contribute</c> role, so without this every Studio operator could read them — and
///     Studio's raw-JSON Config view would display them.
///     </para>
///     <para>
///     Matching is by <b>property name</b>, not by an enumerated list of paths, so a
///     secret added to <c>AppConfig</c> later is covered by default instead of silently
///     becoming readable.
///     </para>
/// </summary>
public static class ConfigRedactor
{
    /// <summary>
    ///     Marker substituted for a secret. Deliberately distinctive so a real value is
    ///     never mistaken for it, and so <see cref="StripRedacted"/> can identify it.
    /// </summary>
    public const string Marker = "__hercules_redacted__";

    /// <summary>
///     Tokens that mark a property as secret-bearing anywhere in its name.
///     <para>
///     Matched as substrings, not exact names, because a live <c>GET /api/config</c> dump
///     showed the exact-name list missing real secrets under compound names —
///     <c>backup.passphrase</c>, <c>nats.authToken</c>, <c>notifications.smtpPassword</c>,
///     <c>edge.enrolmentToken</c> — none of which is literally <c>password</c> or
///     <c>token</c>. Under-redaction is a security defect; over-redaction is cosmetic,
///     so the default is to mask and to un-mask only what is explicitly listed in
///     <see cref="NotSecret"/>.
/// </para>
/// </summary>
private static readonly string[] SecretTokens =
    {
        "apikey",
        "secret",
        "password",
        "passphrase",
        "token",
        "connectionstring",
        "signingkey",
        "privatekey",
        "licensekey",
        "credential",
    };

    /// <summary>
    ///     Names that contain a secret token but hold configuration, not a credential —
    ///     a prefix or a schema name. Kept visible so the config UI stays useful; each one
    ///     was checked against a live config dump.
    /// </summary>
    private static readonly HashSet<string> NotSecret = new(StringComparer.OrdinalIgnoreCase)
    {
        "secretReferencePrefix",
        "distributedKeyPrefix",
        "idempotencyKey",
        // Durations and budgets, not credentials.
        "tokenBudget",
        "tokenTtlSeconds",
        "tokenTtlSec",
    };

    public static bool IsSecretName(string name)
    {
        if (NotSecret.Contains(name)) return false;

        var lowered = name.ToLowerInvariant();
        foreach (var token in SecretTokens)
        {
            if (lowered.Contains(token, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    ///     Replaces secret-named string values with <see cref="Marker"/>. Empty and
    ///     already-null values are left alone, so "no key configured" stays distinguishable
    ///     from "a key is configured but hidden".
    /// </summary>
    public static JsonElement Redact(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object) return config.Clone();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in config.EnumerateObject())
            {
                if (IsSecretName(property.Name))
                {
                    writer.WritePropertyName(property.Name);
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        writer.WriteStringValue(string.IsNullOrEmpty(value) ? "" : Marker);
                    }
                    else
                    {
                        // Non-string secret (object/array/number) is still sensitive:
                        // drop it rather than guess at its shape.
                        writer.WriteNullValue();
                    }
                    continue;
                }

                writer.WritePropertyName(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    WriteRedacted(property.Value, writer);
                    writer.WriteEndObject();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static void WriteRedacted(JsonElement source, Utf8JsonWriter writer)
    {
        foreach (var property in source.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (IsSecretName(property.Name))
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
                writer.WriteStringValue(string.IsNullOrEmpty(value) ? "" : Marker);
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                WriteRedacted(property.Value, writer);
                writer.WriteEndObject();
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }
    }

    /// <summary>
    ///     Removes redacted properties from an incoming patch.
    ///     <para>
    ///     Required so the round trip is safe: Studio reads masked config, lets the operator
    ///     edit unrelated fields and PATCHes the result back. Without this, the marker would
    ///     be merged over the real secret and silently destroy it. A merge patch treats an
    ///     absent property as "unchanged", which is exactly the intent.
    ///     </para>
    /// </summary>
    public static JsonElement StripRedacted(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return patch.Clone();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in patch.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() == Marker)
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    WriteStripped(property.Value, writer);
                    writer.WriteEndObject();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static void WriteStripped(JsonElement source, Utf8JsonWriter writer)
    {
        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString() == Marker)
            {
                continue;
            }

            writer.WritePropertyName(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                WriteStripped(property.Value, writer);
                writer.WriteEndObject();
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }
    }
}