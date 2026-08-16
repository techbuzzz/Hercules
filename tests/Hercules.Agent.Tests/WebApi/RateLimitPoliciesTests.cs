using Hercules.WebApi;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_081: unit-tests for the policy-name constants and the
///     client-key resolver used by the rate-limiter partition function.
/// </summary>
public class RateLimitPoliciesTests
{
    [Fact]
    public void Chat_PolicyName_IsStable()
    {
        Assert.Equal("chat", RateLimitPolicies.Chat);
    }

    [Fact]
    public void Expensive_PolicyName_IsStable()
    {
        Assert.Equal("expensive", RateLimitPolicies.Expensive);
    }

    [Fact]
    public void GetClientKey_PrefersXForwardedFor()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.1";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.99");

        var key = RateLimitPolicies.GetClientKey(ctx);

        Assert.Equal("ip:203.0.113.7", key);
    }

    [Fact]
    public void GetClientKey_FallsBackToRemoteIp()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.42");

        var key = RateLimitPolicies.GetClientKey(ctx);

        Assert.Equal("ip:10.0.0.42", key);
    }

    [Fact]
    public void GetClientKey_ReturnsUnknown_WhenNoAddress()
    {
        var ctx = new DefaultHttpContext();
        // RemoteIpAddress is null by default

        var key = RateLimitPolicies.GetClientKey(ctx);

        Assert.Equal("ip:unknown", key);
    }

    [Fact]
    public void OutputCachePolicies_Skills_IsStable()
    {
        Assert.Equal("Skills", OutputCachePolicies.Skills);
    }

    [Fact]
    public void OutputCachePolicies_Config_IsStable()
    {
        Assert.Equal("Config", OutputCachePolicies.Config);
    }
}
