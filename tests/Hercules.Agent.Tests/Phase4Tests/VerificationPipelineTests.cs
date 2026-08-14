using Hercules.Config;
using Hercules.Mesh.Verification;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase4Tests;

public class SafetyVerifierTests
{
    private readonly SafetyVerifier _verifier = new(Mock.Of<ILogger<SafetyVerifier>>());

    [Fact]
    public async Task VerifyAsync_PassesForNormalText_ReturnsPass()
    {
        var ctx = new VerificationContext { ResponseText = "Hello, how are you?", VerificationId = "test-1" };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.True(result.Passed);
        Assert.Equal("SafetyVerifier", result.VerifierName);
    }

    [Fact]
    public async Task VerifyAsync_EmptyText_ReturnsPass()
    {
        var ctx = new VerificationContext { ResponseText = "", VerificationId = "test-2" };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_CodeInjectionPattern_ReturnsFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "eval($userInput)",
            VerificationId = "test-3"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Equal("SafetyVerifier", result.VerifierName);
        Assert.Contains("code_injection_pattern", result.Reason!);
    }

    [Fact]
    public async Task VerifyAsync_DangerousCommand_ReturnsFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "Running: rm -rf /",
            VerificationId = "test-4"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Contains("dangerous_command", result.Reason!);
    }

    [Fact]
    public async Task VerifyAsync_SuspiciousUrl_ReturnsFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "Found URL: http://localhost/admin/login",
            VerificationId = "test-5"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Contains("suspicious_url", result.Reason!);
    }

    [Fact]
    public async Task VerifyAsync_HighImpactTool_ReturnsFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "File created successfully",
            Mode = "tool",
            ToolUsed = "shell_exec",
            VerificationId = "test-6"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Contains("high_impact_tool", result.Reason!);
    }

    [Fact]
    public async Task VerifyAsync_SafeTool_ReturnsPass()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "Calculation result: 42",
            Mode = "tool",
            ToolUsed = "calculator",
            VerificationId = "test-7"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public void CanVerify_AlwaysTrue()
    {
        var ctx = new VerificationContext();
        Assert.True(_verifier.CanVerify(ctx));
    }
}

public class SchemaVerifierTests
{
    private readonly SchemaVerifier _verifier = new(Mock.Of<ILogger<SchemaVerifier>>());

    [Fact]
    public async Task VerifyAsync_EmptyText_ReturnsFail_LowSeverity()
    {
        // SchemaVerifier checks: empty responses fail schema check with low severity
        var ctx = new VerificationContext { ResponseText = "", VerificationId = "test-1" };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Equal("SCHEMA_EMPTY_RESPONSE", result.ErrorCode);
        Assert.Equal(VerificationSeverity.Low, result.Severity);
    }

    [Fact]
    public async Task VerifyAsync_ValidJson_ReturnsPass()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "{\"result\": \"success\", \"data\": 42}",
            VerificationId = "test-2"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_JsonWithErrorField_ReturnsFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "{\"error\": \"something went wrong\"}",
            Mode = "tool",
            VerificationId = "test-3"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Equal("SCHEMA_ERROR_FIELD", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyAsync_InvalidJson_ReturnsLowSeverityFail()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "{ this is not json }",
            VerificationId = "test-4"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Equal(VerificationSeverity.Low, result.Severity);
    }

    [Fact]
    public async Task VerifyAsync_PlainText_ReturnsPass()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "The weather is sunny today.",
            VerificationId = "test-5"
        };
        var result = await _verifier.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public void CanVerify_AlwaysTrueForNonEmpty()
    {
        var ctx = new VerificationContext { ResponseText = "something" };
        Assert.True(_verifier.CanVerify(ctx));

        var emptyCtx = new VerificationContext { ResponseText = "" };
        Assert.False(_verifier.CanVerify(emptyCtx));
    }
}

public class NumericValidatorTests
{
    private readonly NumericValidator _validator = new(Mock.Of<ILogger<NumericValidator>>());

    [Fact]
    public async Task VerifyAsync_NoNumbers_ReturnsPass()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "The answer is yes.",
            VerificationId = "test-1"
        };
        var result = await _validator.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_ImplausibleFileSize_ReturnsFail()
    {
        // File size of 99999 TB ≈ 1.08e17 bytes > 10 PB threshold
        var ctx = new VerificationContext
        {
            ResponseText = "The file is 99999 TB in size.",
            VerificationId = "test-2"
        };
        var result = await _validator.VerifyAsync(ctx);
        Assert.False(result.Passed);
        Assert.Equal("NUMERIC_IMPLAUSIBLE", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyAsync_ImplausiblePercentage_ReturnsFail()
    {
        // Regex requires space before % and text after: "rate is 99999 % high" matches,
        // then PlausiblePercentage("99999", out _) → pct=99999 > 10000 → implausible
        var ctx = new VerificationContext
        {
            ResponseText = "Success rate is 99999 % in testing",
            VerificationId = "test-3"
        };
        var result = await _validator.VerifyAsync(ctx);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_NormalPercentage_ReturnsPass()
    {
        var ctx = new VerificationContext
        {
            ResponseText = "Success rate: 85%",
            VerificationId = "test-4"
        };
        var result = await _validator.VerifyAsync(ctx);
        Assert.True(result.Passed);
    }

    [Fact]
    public void CanVerify_TrueWhenNumbersPresent()
    {
        // Regex requires digits first: (\d[\d\s.,]*) — "$50." starts with $, not a digit → no match
        // "50 dollars" starts with digit → matches
        var ctx = new VerificationContext { ResponseText = "The cost is 50 dollars." };
        Assert.True(_validator.CanVerify(ctx));
    }
}

public class VerificationPipelineTests
{
    private readonly Mock<ILogger<VerificationPipeline>> _loggerMock = new();

    [Fact]
    public async Task VerifyAsync_DisabledPipeline_ReturnsSuccessWithoutVerifiers()
    {
        var config = new VerificationConfig { Enabled = false };
        var pipeline = new VerificationPipeline(Array.Empty<IVerifier>(), config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.True(result.Passed);
        Assert.Empty(result.VerifierResults);
    }

    [Fact]
    public async Task VerifyAsync_AllVerifiersPass_ReturnsSuccess()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(true);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerifierResult.Pass("MockVerifier"));

        var config = new VerificationConfig { Enabled = true, Mode = "enforce", BlockSeverityThreshold = "high" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.True(result.Passed);
        Assert.Single(result.VerifierResults);
        Assert.True(result.VerifierResults[0].Passed);
    }

    [Fact]
    public async Task VerifyAsync_VerifierFails_HighSeverity_BlocksInEnforceMode()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(true);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerifierResult.Fail("MockVerifier", "Test failure", VerificationSeverity.High, "TEST_FAIL"));

        var config = new VerificationConfig { Enabled = true, Mode = "enforce", BlockSeverityThreshold = "high" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.False(result.Passed);
        Assert.True(result.Blocked);
        Assert.Equal(VerificationSeverity.High, result.MaxSeverity);
        Assert.NotNull(result.BlockingReason);
    }

    [Fact]
    public async Task VerifyAsync_VerifierFails_DryRunMode_AlwaysPasses()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(true);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerifierResult.Fail("MockVerifier", "Test failure", VerificationSeverity.High, "TEST_FAIL"));

        var config = new VerificationConfig { Enabled = true, Mode = "dryrun", BlockSeverityThreshold = "high" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.True(result.Passed);
        Assert.True(result.WasDryRun);
        Assert.Single(result.VerifierResults);
        Assert.False(result.VerifierResults[0].Passed);
    }

    [Fact]
    public async Task VerifyAsync_InapplicableVerifier_Skipped()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(false);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerifierResult.Pass("MockVerifier"));

        var config = new VerificationConfig { Enabled = true, Mode = "enforce" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.True(result.Passed);
        Assert.Empty(result.VerifierResults);
        mockVerifier.Verify(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_VerifierThrows_TreatedAsPass()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(true);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var config = new VerificationConfig { Enabled = true, Mode = "enforce" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        // Should pass with best-effort (verifier threw)
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_LowSeverity_NotBlocked()
    {
        var mockVerifier = new Mock<IVerifier>();
        mockVerifier.Setup(v => v.Name).Returns("MockVerifier");
        mockVerifier.Setup(v => v.CanVerify(It.IsAny<VerificationContext>())).Returns(true);
        mockVerifier.Setup(v => v.VerifyAsync(It.IsAny<VerificationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerifierResult.Fail("MockVerifier", "Minor issue", VerificationSeverity.Low, "MINOR"));

        var config = new VerificationConfig { Enabled = true, Mode = "enforce", BlockSeverityThreshold = "medium" };
        var pipeline = new VerificationPipeline(new[] { mockVerifier.Object }, config, _loggerMock.Object);

        var ctx = new VerificationContext { ResponseText = "test", VerificationId = "test-1" };
        var result = await pipeline.VerifyAsync(ctx);

        Assert.True(result.Passed);
    }

    [Fact]
    public void VerificationResult_FactoryMethods_Work()
    {
        var pass = VerificationResult.Success("v1", Array.Empty<VerifierResult>(), 10);
        Assert.True(pass.Passed);
        Assert.False(pass.Blocked);
        Assert.Equal(10, pass.ElapsedMs);

        var blocked = VerificationResult.BlockedResult("v2",
            new[] { VerifierResult.Fail("v", "test", VerificationSeverity.High, "ERR") },
            VerificationSeverity.High, "test failure", 5);
        Assert.False(blocked.Passed);
        Assert.True(blocked.Blocked);
        Assert.Equal(VerificationSeverity.High, blocked.MaxSeverity);
    }
}
