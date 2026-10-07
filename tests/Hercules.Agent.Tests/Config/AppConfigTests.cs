using System.Text.Json;
using Hercules.Config;
using Xunit;

namespace Hercules.Agent.Tests.Config;

/// <summary>
///     Тесты AppConfig: defaults, JSON round-trip, partial JSON,
///     nested config, dictionary config.
/// </summary>
public class AppConfigTests
{
    [Fact]
    public void Default_Config_HasReasonableDefaults()
    {
        var cfg = new AppConfig();

        Assert.NotNull(cfg.Llm);
        Assert.NotNull(cfg.Storage);
        Assert.NotNull(cfg.Agent);
        Assert.NotNull(cfg.Telegram);
        Assert.NotNull(cfg.CodeExecution);
        Assert.NotNull(cfg.Http);
        Assert.NotNull(cfg.Mcp);
        Assert.NotNull(cfg.A2A);
        Assert.NotNull(cfg.Mesh);
        Assert.NotNull(cfg.Roles);
        Assert.NotNull(cfg.Phase2);
    }

    [Fact]
    public void StorageConfig_HasReasonableDefaults()
    {
        var cfg = new StorageConfig();

        // R18/R3a: DataRoot is NOT the relative literal "data" any more. Since the
        // data-root relocation it is resolved at construction via
        // BuiltIn.ResolveDataRoot(), which always returns an absolute path OUTSIDE
        // the repository. Asserting the old literal made this test machine-dependent.
        Assert.Equal(BuiltIn.ResolveDataRoot(), cfg.DataRoot);
        Assert.True(Path.IsPathFullyQualified(cfg.DataRoot),
            $"DataRoot must be absolute, got '{cfg.DataRoot}'");

        Assert.Equal("Memory", cfg.MemoryDir);
        Assert.Equal("Skills", cfg.SkillsDir);
        Assert.Equal("sessions.db", cfg.SqliteFile);
    }

    [Fact]
    public void StorageConfig_DataRoot_HonorsEnvironmentOverride()
    {
        // R3a: HERCULES_DATA_ROOT must win over repo/user-profile resolution. This is
        // what lets CI pin the suite's data outside the checkout.
        var previous = Environment.GetEnvironmentVariable(BuiltIn.DataRootEnvironmentVariable);
        var tempRoot = Path.Combine(Path.GetTempPath(), "hercules-dataroottest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(BuiltIn.DataRootEnvironmentVariable, tempRoot);

            var resolved = BuiltIn.ResolveDataRoot();

            Assert.Equal(Path.GetFullPath(tempRoot), resolved);
            Assert.Equal(resolved, new StorageConfig().DataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BuiltIn.DataRootEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void RoundTrip_SerializesAndDeserializes()
    {
        var original = new AppConfig
        {
            Llm = new LlmConfig { Provider = "yandexgpt", Fallback = ["ollama-cloud"] },
            Agent = new AgentConfig { ReflectionEveryNCommands = 5, SystemPrompt = "Ты — ассистент." }
        };

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        var deserialized = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(deserialized);
        Assert.Equal("yandexgpt", deserialized.Llm.Provider);
        Assert.Single(deserialized.Llm.Fallback);
        Assert.Equal("ollama-cloud", deserialized.Llm.Fallback[0]);
        Assert.Equal(5, deserialized.Agent.ReflectionEveryNCommands);
        Assert.Equal("Ты — ассистент.", deserialized.Agent.SystemPrompt);
    }

    [Fact]
    public void PartialJson_IgnoresUnknownProperties()
    {
        var partial = """
            {
                "llm": { "provider": "ollama-local" },
                "unknownField": "should be ignored"
            }
            """;

        var cfg = JsonSerializer.Deserialize<AppConfig>(partial, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(cfg);
        Assert.Equal("ollama-local", cfg.Llm.Provider);
    }

    [Fact]
    public void AgentConfig_SkillThresholds_AreSet()
    {
        var cfg = new AgentConfig();

        Assert.True(cfg.ReflectionEveryNCommands >= 0);
        Assert.True(cfg.SkillCreationThreshold >= 0);
    }

    [Fact]
    public void Phase2Config_HasSemanticRoutingDefault()
    {
        var cfg = new Phase2Config();

        Assert.False(cfg.SemanticRoutingEnabled); // default false
    }

    [Fact]
    public void MeshConfig_HasReasonableDefaults()
    {
        var cfg = new MeshConfig();

        Assert.Equal("hercules-main", cfg.AgentId);
        Assert.Equal("Hercules", cfg.DisplayName);
        Assert.False(cfg.Enabled); // default false
        Assert.NotNull(cfg.Endpoint);
    }
}
