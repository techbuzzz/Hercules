using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Hercules.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_081: end-to-end integration tests for the framework rate limiter,
///     response compression, and output cache. We stand up a minimal
///     <see cref="WebApplication" /> via <see cref="TestServer" /> instead of
///     booting the full Hercules WebApi, which avoids pulling in LLM / disk /
///     database dependencies while still exercising the same middleware stack.
/// </summary>
public class KestrelRateLimitCachePipelineTests
{
    private static IHost BuildTestHost(int chatPermits, int chatWindowSec = 60, int expensivePermits = 2)
    {
        // Counter incremented by the (uncached) skills endpoint to verify the cache
        // is actually serving the second response.
        var callCounter = 0;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Mirror the production wiring (task_081).
        builder.Services.AddRateLimiter(o =>
        {
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                    context.HttpContext.Response.Headers["Retry-After"] = seconds.ToString();
                    context.HttpContext.Response.Headers["X-RateLimit-Reset"] = seconds.ToString();
                }
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Rate limit exceeded. Try again later.\"}", ct);
            };

            o.AddPolicy(RateLimitPolicies.Chat, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPolicies.GetClientKey(http),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = chatPermits,
                        Window = TimeSpan.FromSeconds(chatWindowSec),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            o.AddPolicy(RateLimitPolicies.Expensive, _ =>
                RateLimitPartition.GetConcurrencyLimiter("expensive", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = expensivePermits,
                    QueueLimit = 0
                }));
        });

        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = new[] { "application/json", "text/plain" };
        });

        builder.Services.AddOutputCache(o =>
        {
            o.AddBasePolicy(b => b.Expire(TimeSpan.FromMinutes(5)));
            o.AddPolicy(OutputCachePolicies.Skills, b => b
                .Expire(TimeSpan.FromMinutes(5))
                .Tag("skills"));
            o.AddPolicy(OutputCachePolicies.Config, b => b
                .Expire(TimeSpan.FromSeconds(30))
                .Tag("config"));
        });

        var app = builder.Build();
        app.UseRateLimiter();
        app.UseResponseCompression();
        app.UseOutputCache();

        app.MapGet("/api/chat", () => Results.Ok(new { ok = true }))
            .RequireRateLimiting(RateLimitPolicies.Chat);

        app.MapGet("/api/skills", () =>
            {
                var n = Interlocked.Increment(ref callCounter);
                return Results.Ok(new { skills = Array.Empty<string>(), call = n });
            })
            .CacheOutput(OutputCachePolicies.Skills);

        app.MapGet("/api/config", () => Results.Ok(new { config = new { v = 1 } }))
            .CacheOutput(OutputCachePolicies.Config);

        app.MapGet("/api/expensive", async () =>
        {
            await Task.Delay(50);
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting(RateLimitPolicies.Expensive);

        // Expose the counter for the test to assert against.
        app.MapGet("/__callcount", () => Results.Ok(new { count = callCounter }));

        return app;
    }

    private static async Task<HttpClient> NewClientAsync(IHost host)
    {
        await host.StartAsync();
        return host.GetTestClient();
    }

    // -------- Rate limiter: chat --------

    [Fact]
    public async Task ChatRateLimit_AllowsUpToPermitLimit_ThenRejects429()
    {
        using var host = BuildTestHost(chatPermits: 3, chatWindowSec: 60);
        var client = await NewClientAsync(host);

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.GetAsync("/api/chat");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await client.GetAsync("/api/chat");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ChatRateLimit_RejectedResponse_ContainsRetryAfter()
    {
        using var host = BuildTestHost(chatPermits: 1, chatWindowSec: 60);
        var client = await NewClientAsync(host);

        // Burn the only permit.
        var first = await client.GetAsync("/api/chat");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var rejected = await client.GetAsync("/api/chat");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // The framework rate limiter must surface Retry-After so clients can back off.
        Assert.True(rejected.Headers.Contains("Retry-After"),
            $"Expected Retry-After header on 429 response. Headers: {string.Join(",", rejected.Headers.Select(h => h.Key))}");
    }

    // -------- Rate limiter: concurrency --------

    [Fact]
    public async Task ExpensiveRateLimit_RejectsWhenConcurrencyExhausted()
    {
        using var host = BuildTestHost(chatPermits: 100, chatWindowSec: 60, expensivePermits: 1);
        var client = await NewClientAsync(host);

        // Launch 2 slow requests — first should pass, second should be rejected.
        var t1 = client.GetAsync("/api/expensive");
        await Task.Delay(20); // let the first request start
        var t2 = client.GetAsync("/api/expensive");

        var responses = await Task.WhenAll(t1, t2);

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    // -------- Response compression --------

    [Fact]
    public async Task ResponseCompression_BrotliAccepted_ReturnsBrContentEncoding()
    {
        using var host = BuildTestHost(chatPermits: 100);
        var client = await NewClientAsync(host);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/skills");
        req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // Brotli is what the client asked for, so it should be the selected encoding.
        Assert.NotNull(resp.Content.Headers.ContentEncoding);
        Assert.Contains("br", resp.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ResponseCompression_GzipAccepted_ReturnsGzipContentEncoding()
    {
        using var host = BuildTestHost(chatPermits: 100);
        var client = await NewClientAsync(host);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/skills");
        req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(resp.Content.Headers.ContentEncoding);
        Assert.Contains("gzip", resp.Content.Headers.ContentEncoding);
    }

    // -------- Output cache --------

    [Fact]
    public async Task OutputCache_FirstRequestMisses_SecondHits()
    {
        using var host = BuildTestHost(chatPermits: 100);
        var client = await NewClientAsync(host);

        var first = await client.GetAsync("/api/skills");
        var second = await client.GetAsync("/api/skills");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // The counter is incremented inside the endpoint body; if the second
        // request is served from the output cache, the counter must NOT advance.
        var countResp = await client.GetAsync("/__callcount");
        var countJson = await countResp.Content.ReadAsStringAsync();
        Assert.Contains("\"count\":1", countJson);

        // Both responses should also share the same body — the cached snapshot
        // was produced by the first call (call=1).
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Contains("\"call\":1", firstBody);
        Assert.Equal(firstBody, secondBody);
    }
}
