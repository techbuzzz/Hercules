using Hercules.Config;
using Hercules.Redaction;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Config;

/// <summary>
///     Тесты SecretMaskingService: masking, secret resolution, reference detection (task_015).
/// </summary>
public class SecretMaskingServiceTests
{
    private readonly SecretMaskingService _svc;
    private readonly RedactionService _redaction;
    private readonly SecretsConfig _config;

    public SecretMaskingServiceTests()
    {
        _config = new SecretsConfig
        {
            EnvironmentVariablePrefix = "HERCULES_SECRET_",
            SecretReferencePrefix = "env:",
            RedactInExports = true,
            RedactInMemory = true,
            RedactInTelemetry = true
        };
        var loggerMock = new Mock<ILogger<RedactionService>>();
        _redaction = new RedactionService(new AuditConfig(), loggerMock.Object);
        _svc = new SecretMaskingService(_config, _redaction);
    }

    // ---- MaskSecrets tests ----

    [Fact]
    public void MaskSecrets_ApiKey_ReturnsMasked()
    {
        var result = _svc.MaskSecrets("Authorization: Bearer sk-abc123xyz");
        Assert.DoesNotContain("sk-abc123xyz", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void MaskSecrets_Email_ReturnsMasked()
    {
        var result = _svc.MaskSecrets("Send to: test@example.com");
        Assert.DoesNotContain("test@example.com", result);
        Assert.Contains("[EMAIL]", result);
    }

    [Fact]
    public void MaskSecrets_PhoneNumber_ReturnsMasked()
    {
        var result = _svc.MaskSecrets("Call me: +1-555-123-4567");
        Assert.DoesNotContain("555-123-4567", result);
        Assert.Contains("[PHONE]", result);
    }

    [Fact]
    public void MaskSecrets_CreditCard_ReturnsMasked()
    {
        var result = _svc.MaskSecrets("Card: 4111-1111-1111-1111");
        Assert.DoesNotContain("4111-1111-1111-1111", result);
        Assert.Contains("[CARD]", result);
    }

    [Fact]
    public void MaskSecrets_BearerToken_ReturnsMasked()
    {
        var result = _svc.MaskSecrets("Authorization: Bearer sk-test-1234567890abcdef");
        Assert.DoesNotContain("sk-test-1234567890abcdef", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void MaskSecrets_EmptyString_ReturnsEmpty()
    {
        var result = _svc.MaskSecrets("");
        Assert.Equal("", result);
    }

    [Fact]
    public void MaskSecrets_Null_ReturnsNull()
    {
        var result = _svc.MaskSecrets(null!);
        Assert.Equal("", result);
    }

    [Fact]
    public void MaskSecrets_CleanText_ReturnsUnchanged()
    {
        var input = "Hello, this is a clean text with no secrets.";
        var result = _svc.MaskSecrets(input);
        Assert.Equal(input, result);
    }

    // ---- ResolveSecret tests ----

    [Fact]
    public void ResolveSecret_EnvPrefix_ResolvesVariable()
    {
        // Arrange: set a test env var with the HERCULES_SECRET_ prefix
        var varName = "HERCULES_SECRET_MYAPIKEY";
        var expectedValue = "secret_value_12345";
        Environment.SetEnvironmentVariable(varName, expectedValue);
        try
        {
            var result = _svc.ResolveSecret("env:MYAPIKEY");
            Assert.Equal(expectedValue, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void ResolveSecret_CaseInsensitive_ResolvesVariable()
    {
        var varName = "HERCULES_SECRET_CASEINS";
        Environment.SetEnvironmentVariable(varName, "case_insensitive_value");
        try
        {
            // Lookup should be case-insensitive
            var result = _svc.ResolveSecret("env:caseins");
            Assert.Equal("case_insensitive_value", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void ResolveSecret_NonExistent_ReturnsNull()
    {
        var result = _svc.ResolveSecret("env:THIS_VARIABLE_DEFINITELY_DOES_NOT_EXIST_12345");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveSecret_NotReference_ReturnsNull()
    {
        var result = _svc.ResolveSecret("Bearer token-value");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveSecret_EmptyString_ReturnsNull()
    {
        var result = _svc.ResolveSecret("");
        Assert.Null(result);
    }

    // ---- IsSecretReference tests ----

    [Fact]
    public void IsSecretReference_EnvPrefix_ReturnsTrue()
    {
        Assert.True(_svc.IsSecretReference("env:MY_SECRET"));
    }

    [Fact]
    public void IsSecretReference_PlainText_ReturnsFalse()
    {
        Assert.False(_svc.IsSecretReference("Bearer token123"));
    }

    [Fact]
    public void IsSecretReference_Empty_ReturnsFalse()
    {
        Assert.False(_svc.IsSecretReference(""));
    }

    [Fact]
    public void IsSecretReference_Null_ReturnsFalse()
    {
        Assert.False(_svc.IsSecretReference(null!));
    }

    // ---- ExpandSecretReferences tests ----

    [Fact]
    public void ExpandSecretReferences_Reference_ExpandsToValue()
    {
        var varName = "HERCULES_SECRET_APISECRET";
        Environment.SetEnvironmentVariable(varName, "my_api_secret");
        try
        {
            var result = _svc.ExpandSecretReferences("API key is env:APISECRET");
            Assert.Contains("my_api_secret", result);
            Assert.DoesNotContain("env:APISECRET", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void ExpandSecretReferences_NonExistentReference_LeavesUnchanged()
    {
        var result = _svc.ExpandSecretReferences("Key: env:NONEXISTENT_VAR_XYZ");
        Assert.Contains("env:NONEXISTENT_VAR_XYZ", result);
    }

    [Fact]
    public void ExpandSecretReferences_NoReferences_ReturnsOriginal()
    {
        var input = "No references here.";
        var result = _svc.ExpandSecretReferences(input);
        Assert.Equal(input, result);
    }

    // ---- Integration with AuditConfig ----

    [Fact]
    public void MaskSecrets_CustomPatternFromAuditConfig_RedactsCustomPattern()
    {
        var auditCfg = new AuditConfig
        {
            RedactionPatterns = new List<AuditRedactionPattern>
            {
                new() { Pattern = @"\bproject_id[=:]\s*\w+\b", Replacement = "[PROJECT_ID]" }
            }
        };
        var redactionSvc = new RedactionService(auditCfg, Mock.Of<ILogger<RedactionService>>());
        var svc = new SecretMaskingService(_config, redactionSvc);

        var result = svc.MaskSecrets("Setting project_id=abc123 in config");
        Assert.Contains("[PROJECT_ID]", result);
        Assert.DoesNotContain("abc123", result);
    }

    // ---- Backward compatibility: null redaction service ----

    [Fact]
    public void Constructor_NullRedactionService_DoesNotThrow()
    {
        var cfg = new SecretsConfig();
        var svc = new SecretMaskingService(cfg, null!);
        // Should not throw even with null redaction service
        var ex = Record.Exception(() => svc.MaskSecrets("Bearer secret_token_value_1234567890abcdef"));
        Assert.Null(ex);
    }

    [Fact]
    public void MaskSecrets_MultipleSecretsInText_RedactsAll()
    {
        var result = _svc.MaskSecrets("Email: user@test.com, Phone: +1-555-123-4567, Card: 4111-1111-1111-1111");
        Assert.DoesNotContain("user@test.com", result);
        Assert.DoesNotContain("555-123-4567", result);
        Assert.DoesNotContain("4111-1111-1111-1111", result);
    }

    [Fact]
    public void MaskSecrets_InlineSecretLikePattern_RedactsApiKey()
    {
        // API key pattern: \b(sk|pk|token|secret|key|auth)[_-][a-zA-Z0-9]{10,}
        // Use a token that definitely doesn't start with "Bearer "
        var result = _svc.MaskSecrets("token-key_abcdefghijklmnop");
        Assert.DoesNotContain("abcdefghijklmnop", result);
        Assert.Contains("[API_KEY]", result);
    }
}
