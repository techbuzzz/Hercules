using System.Text.Json;
using Hercules.WebApi.Config;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     <c>GET /api/config</c> returns the whole <c>AppConfig</c> and is readable by any
///     authenticated session, including contribute role. <c>AppConfig</c> embeds real
///     credentials — LLM provider API keys, the Postgres session-store connection string
///     (which carries a password), a Telegram bot token and a signing key — so the
///     endpoint must not serialise them verbatim.
/// </summary>
public class ConfigRedactorTests
{
    private const string RealKey = "sk-live-DO-NOT-LEAK";
    private const string RealConnString = "Host=db.internal;Username=hercules;Password=hunter2";
    private const string RealBotToken = "123456789:AAH-bot-token-DO-NOT-LEAK";

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void Redact_ReplacesEverySecretAppConfigCarries()
    {
        // Built from a real AppConfig so a future field rename cannot quietly skip a case:
        // LLM provider keys (4 paths), the Postgres session-store connection string and the
        // Telegram bot token all flow through the same serializer as the live endpoint.
        var config = new Hercules.Config.AppConfig
        {
            Llm = new Hercules.Config.LlmConfig
            {
                Provider = "yandexgpt",
                YandexGpt = { ApiKey = RealKey },
                OllamaCloud = { ApiKey = RealKey },
                OllamaLocal = { ApiKey = RealKey },
                OpenAICompatible = { ApiKey = RealKey },
            },
            Storage = new Hercules.Config.StorageConfig
            {
                SessionStore = { Provider = "postgres", ConnectionString = RealConnString }
            },
            Telegram = new Hercules.Config.TelegramConfig { BotToken = RealBotToken },
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        var element = JsonSerializer.SerializeToElement(config, options);

        var redacted = ConfigRedactor.Redact(element);
        var text = redacted.GetRawText();

        Assert.DoesNotContain(RealKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(RealConnString, text, StringComparison.Ordinal);
        Assert.DoesNotContain(RealBotToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.Contains(ConfigRedactor.Marker, text, StringComparison.Ordinal);

        // Non-secret settings must survive, or the endpoint stops being useful.
        Assert.Contains("yandexgpt", text, StringComparison.Ordinal);
        Assert.Equal("postgres", redacted.GetProperty("storage").GetProperty("sessionStore").GetProperty("provider").GetString());
    }

    [Fact]
    public void Redact_KeepsAnEmptySecretDistinguishableFromAHiddenOne()
    {
        var redacted = ConfigRedactor.Redact(Json("""{"llm":{"apiKey":"secret-value"},"empty":{"apiKey":""}}"""));

        Assert.Equal(ConfigRedactor.Marker, redacted.GetProperty("llm").GetProperty("apiKey").GetString());
        Assert.Equal("", redacted.GetProperty("empty").GetProperty("apiKey").GetString());
    }

    [Fact]
    public void Redact_DoesNotOverreachOnConfigurationThatMerelyLooksSecret()
    {
        var redacted = ConfigRedactor.Redact(Json(
            """{"distributedKeyPrefix":"quota:","idempotencyKey":{"ttlSeconds":30},"secretReferencePrefix":"env:"}"""));

        Assert.Equal("quota:", redacted.GetProperty("distributedKeyPrefix").GetString());
        Assert.Equal(30, redacted.GetProperty("idempotencyKey").GetProperty("ttlSeconds").GetInt32());
        Assert.Equal("env:", redacted.GetProperty("secretReferencePrefix").GetString());
    }

    /// <summary>
    /// Found by running against a live agent: the exact-name matcher missed real secrets
    /// under compound names. Every name below appeared in a real GET /api/config dump.
    /// </summary>
    [Theory]
    [InlineData("passphrase")]
    [InlineData("authToken")]
    [InlineData("smtpPassword")]
    [InlineData("enrolmentToken")]
    [InlineData("telegramBotToken")]
    [InlineData("apiKey")]
    [InlineData("connectionString")]
    public void IsSecretName_CatchesCompoundSecretNames(string name)
    {
        Assert.True(ConfigRedactor.IsSecretName(name));
    }

    [Fact]
    public void Redact_MasksCompoundSecretNamesInPlace()
    {
        var redacted = ConfigRedactor.Redact(Json(
            """{"backup":{"passphrase":"correct horse battery"},"nats":{"authToken":"nats-secret"},"notifications":{"smtpPassword":"mail-pw"}}"""));

        var text = redacted.GetRawText();
        Assert.DoesNotContain("correct horse battery", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nats-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mail-pw", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSecretName_IsExactSoPrefixedSettingsAreNotCaught()
    {
        Assert.True(ConfigRedactor.IsSecretName("apiKey"));
        Assert.True(ConfigRedactor.IsSecretName("connectionString"));
        Assert.False(ConfigRedactor.IsSecretName("distributedKeyPrefix"));
        Assert.False(ConfigRedactor.IsSecretName("idempotencyKey"));
        Assert.False(ConfigRedactor.IsSecretName("secretReferencePrefix"));
        Assert.False(ConfigRedactor.IsSecretName("tokenTtlSeconds"));
    }

    [Fact]
    public void Redact_LeavesNonObjectInputAlone()
    {
        var scalar = Json("[1,2,3]");
        Assert.Equal(JsonValueKind.Array, ConfigRedactor.Redact(scalar).ValueKind);
    }

    [Fact]
    public void StripRedacted_RemovesTheMarkerSoTheRealSecretSurvivesTheRoundTrip()
    {
        // Exactly what Studio does: read masked config, edit something else, patch back.
        var marker = ConfigRedactor.Marker;
        var patch = Json(
            "{\"llm\":{\"provider\":\"ollama\",\"apiKey\":\"" + marker + "\"},"
            + "\"storage\":{\"sessionStore\":{\"connectionString\":\"" + marker + "\"}}}");

        var stripped = ConfigRedactor.StripRedacted(patch);

        Assert.Equal("ollama", stripped.GetProperty("llm").GetProperty("provider").GetString());
        Assert.False(stripped.GetProperty("llm").TryGetProperty("apiKey", out _));
        Assert.False(stripped.GetProperty("storage").GetProperty("sessionStore").TryGetProperty("connectionString", out _));
    }

    [Fact]
    public void StripRedacted_KeepsARealValueTheOperatorActuallyWantsToSet()
    {
        var patch = Json("""{"llm":{"apiKey":"a-brand-new-key"}}""");

        var stripped = ConfigRedactor.StripRedacted(patch);

        Assert.Equal("a-brand-new-key", stripped.GetProperty("llm").GetProperty("apiKey").GetString());
    }
}