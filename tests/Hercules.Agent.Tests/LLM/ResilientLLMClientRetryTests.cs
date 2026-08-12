using System.Net;
using Hercules.Config;
using Hercules.LLM;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

/// <summary>
/// Тесты retry-логики в ResilientLLMClient.
/// Основной тест — IsRetryable: коды 429/500/502/503/504 ретраятся, остальные — нет.
/// </summary>
public class ResilientLLMClientRetryTests
{
    // ---- IsRetryable: retryable HTTP codes ----

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]         // 429
    [InlineData(HttpStatusCode.InternalServerError)]     // 500
    [InlineData(HttpStatusCode.BadGateway)]               // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]       // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]          // 504
    public void IsRetryable_returns_true_for_retryable_codes(HttpStatusCode status)
    {
        var ex = new HttpRequestException("test", null, status);
        Assert.True(ResilientLLMClient.IsRetryable(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]    // 401
    [InlineData(HttpStatusCode.Forbidden)]        // 403
    [InlineData(HttpStatusCode.NotFound)]        // 404
    [InlineData(HttpStatusCode.BadRequest)]       // 400
    [InlineData(HttpStatusCode.MethodNotAllowed)] // 405
    public void IsRetryable_returns_false_for_non_retryable_codes(HttpStatusCode status)
    {
        var ex = new HttpRequestException("test", null, status);
        Assert.False(ResilientLLMClient.IsRetryable(ex));
    }

    [Fact]
    public void IsRetryable_returns_false_for_OperationCanceledException()
    {
        var ex = new OperationCanceledException();
        Assert.False(ResilientLLMClient.IsRetryable(ex));
    }

    [Fact]
    public void IsRetryable_returns_true_for_network_error()
    {
        // Network-level IOException (no HTTP status)
        var ex = new HttpRequestException("Network error");
        Assert.True(ResilientLLMClient.IsRetryable(ex));
    }

    [Fact]
    public void IsRetryable_returns_true_when_nested_in_aggregate_or_HttpRequestException_with_status()
    {
        // Simulate OpenAI SDK wrapping: HttpRequestException wrapping another HttpRequestException
        var inner = new HttpRequestException("inner", null, HttpStatusCode.ServiceUnavailable);
        var outer = new HttpRequestException("outer", inner);
        Assert.True(ResilientLLMClient.IsRetryable(outer));
    }
}
