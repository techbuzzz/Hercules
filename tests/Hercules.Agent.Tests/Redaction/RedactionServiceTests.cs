using Hercules.Audit;
using Hercules.Config;
using Hercules.Redaction;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Redaction;

/// <summary>
///     Тесты RedactionService: API key, email, phone, credit card redaction.
/// </summary>
public class RedactionServiceTests
{
    private readonly RedactionService _svc;

    public RedactionServiceTests()
    {
        var config = new AuditConfig();
        var loggerMock = new Mock<ILogger<RedactionService>>();
        _svc = new RedactionService(config, loggerMock.Object);
    }

    // ---- High sensitivity tests ----

    [Fact]
    public void Redact_HighSensitivity_RedactsApiKeys()
    {
        var result = _svc.Redact("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", sensitivity: "high");
        Assert.DoesNotContain("eyJ", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Redact_HighSensitivity_RedactsEmails()
    {
        var result = _svc.Redact("Contact: alice@example.com", sensitivity: "high");
        Assert.DoesNotContain("alice@example.com", result);
        Assert.Contains("[EMAIL]", result);
    }

    [Fact]
    public void Redact_HighSensitivity_RedactsPhoneNumbers()
    {
        var result = _svc.Redact("Call: +1-555-123-4567", sensitivity: "high");
        Assert.DoesNotContain("555", result);
        Assert.Contains("[PHONE]", result);
    }

    [Fact]
    public void Redact_HighSensitivity_RedactsCreditCards()
    {
        // Use dashes so the credit card pattern matches (phone pattern won't match due to separators)
        var result = _svc.Redact("Card: 4111-1111-1111-1111", sensitivity: "high");
        Assert.DoesNotContain("4111-1111-1111-1111", result);
        Assert.Contains("[CARD]", result);
    }

    [Fact]
    public void Redact_HighSensitivity_RedactsBearerTokens()
    {
        var result = _svc.Redact("Authorization: Bearer sk-abc123xyz789012345", sensitivity: "high");
        Assert.DoesNotContain("sk-abc123xyz789012345", result);
        Assert.Contains("Bearer ***", result);
    }

    // ---- Medium sensitivity tests ----

    [Fact]
    public void Redact_MediumSensitivity_RedactsApiKeys()
    {
        var result = _svc.Redact("token=sk-1234567890abcdefghij", sensitivity: "medium");
        Assert.DoesNotContain("sk-1234567890", result);
    }

    [Fact]
    public void Redact_MediumSensitivity_RedactsEmails()
    {
        var result = _svc.Redact("user@company.org logged in", sensitivity: "medium");
        Assert.DoesNotContain("user@company.org", result);
    }

    [Fact]
    public void Redact_MediumSensitivity_RedactsPhones()
    {
        var result = _svc.Redact("phone: (555) 123-4567", sensitivity: "medium");
        Assert.DoesNotContain("555", result);
    }

    // ---- Low sensitivity tests ----

    [Fact]
    public void Redact_LowSensitivity_RedactsOnlyApiKeysAndTokens()
    {
        var result = _svc.Redact("user@email.com phone 555-1234", sensitivity: "low");
        // Email and phone should NOT be redacted at low sensitivity
        Assert.Contains("user@email.com", result);
        Assert.Contains("555", result);
    }

    [Fact]
    public void Redact_LowSensitivity_RedactsBearerTokens()
    {
        var result = _svc.Redact("key=sk-test1234567890", sensitivity: "low");
        Assert.Contains("[API_KEY]", result);
    }

    // ---- Empty/null input ----

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("no secrets here")]
    public void Redact_NoSecrets_ReturnsUnchanged(string input)
    {
        var result = _svc.Redact(input, sensitivity: "high");
        Assert.Equal(input, result);
    }

    // ---- Custom patterns ----

    [Fact]
    public void Redact_CustomPattern_ReplacesMatch()
    {
        var config = new AuditConfig
        {
            RedactionPatterns = new List<AuditRedactionPattern>
            {
                new() { Pattern = @"password[=:]\w+", Replacement = "password=***" }
            }
        };
        var loggerMock = new Mock<ILogger<RedactionService>>();
        var svc = new RedactionService(config, loggerMock.Object);

        var result = svc.Redact("db password=SuperSecret123", sensitivity: "high");

        Assert.DoesNotContain("SuperSecret123", result);
        Assert.Contains("password=***", result);
    }

    [Fact]
    public void Redact_CustomPattern_InvalidRegex_SwallowsError()
    {
        var config = new AuditConfig
        {
            RedactionPatterns = new List<AuditRedactionPattern>
            {
                new() { Pattern = @"[invalid", Replacement = "x" }
            }
        };
        var loggerMock = new Mock<ILogger<RedactionService>>();
        var svc = new RedactionService(config, loggerMock.Object);

        // Should not throw
        var result = svc.Redact("some text with [invalid pattern", sensitivity: "high");
        Assert.NotNull(result);
    }

    // ---- RedactAll ----

    [Fact]
    public void RedactAll_RedactsAllBuiltInPatterns()
    {
        // Bearer token needs 10+ chars after "sk-"; email and credit card with dashes
        var result = _svc.RedactAll("Bearer sk-1234567890abcdefgh | email@test.com | Card 4111-1111-1111-1111");
        Assert.DoesNotContain("sk-1234567890abc", result);
        Assert.Contains("[EMAIL]", result);
        Assert.Contains("[CARD]", result);
    }
}
