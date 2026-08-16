using System.IO;
using System.Text;
using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     task_081: unit-tests for the new defense-in-depth request body limit
///     (LimitedStream + RequestBodyLimitMiddleware). Validates chunked uploads
///     are rejected when total bytes exceed the configured cap.
/// </summary>
public class LimitedStreamTests
{
    [Fact]
    public void Read_UnderLimit_ReturnsAllBytes()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        using var limited = new LimitedStream(inner, maxBytes: 64, NullLogger.Instance, "/api/x", "POST");

        var bytes = new byte[64];
        var n = limited.Read(bytes, 0, bytes.Length);

        Assert.Equal(11, n);
        Assert.Equal("hello world", Encoding.UTF8.GetString(bytes, 0, n));
    }

    [Fact]
    public void Read_ExceedsLimit_Throws()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 128)));
        using var limited = new LimitedStream(inner, maxBytes: 16, NullLogger.Instance, "/api/x", "POST");

        Assert.Throws<InvalidDataException>(() =>
        {
            var bytes = new byte[128];
            limited.Read(bytes, 0, bytes.Length);
        });
    }

    [Fact]
    public async Task ReadAsync_ExceedsLimit_Throws()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 128)));
        using var limited = new LimitedStream(inner, maxBytes: 16, NullLogger.Instance, "/api/x", "POST");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            var bytes = new byte[128];
            await limited.ReadAsync(bytes);
        });
    }

    [Fact]
    public async Task ReadAsync_UnderLimit_ReturnsAllBytes()
    {
        var data = Encoding.UTF8.GetBytes("payload");
        using var inner = new MemoryStream(data);
        using var limited = new LimitedStream(inner, maxBytes: 64, NullLogger.Instance, "/api/x", "POST");

        var bytes = new byte[64];
        var n = await limited.ReadAsync(bytes);

        Assert.Equal(data.Length, n);
        Assert.Equal("payload", Encoding.UTF8.GetString(bytes, 0, n));
    }

    [Fact]
    public void Read_InChunks_AccumulatesAndThrowsAtLimit()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 64)));
        using var limited = new LimitedStream(inner, maxBytes: 32, NullLogger.Instance, "/api/x", "POST");

        // First read of 16 bytes is fine.
        var buf1 = new byte[16];
        var n1 = limited.Read(buf1, 0, 16);
        Assert.Equal(16, n1);

        // Second read of 16 bytes brings the counter to 32 — still allowed.
        var buf2 = new byte[16];
        var n2 = limited.Read(buf2, 0, 16);
        Assert.Equal(16, n2);

        // Third read of any size pushes us over the cap.
        Assert.Throws<InvalidDataException>(() => limited.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void Write_ThrowsNotSupported()
    {
        using var inner = new MemoryStream();
        using var limited = new LimitedStream(inner, 64, NullLogger.Instance, "/api/x", "POST");
        Assert.Throws<NotSupportedException>(() => limited.Write(new byte[1], 0, 1));
    }
}

/// <summary>
///     Integration-style test that wires up the middleware against an inline
///     <see cref="RequestDelegate" /> and checks the HTTP-level behaviour.
/// </summary>
public class RequestBodyLimitMiddlewareTests
{
    private static (RequestBodyLimitMiddleware middleware, HttpContext ctx) Arrange(
        long maxBytes,
        long? contentLength,
        byte[] body,
        string path = "/api/test")
    {
        var cfg = new WebApiConfig { MaxRequestBodyBytes = maxBytes };
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new RequestBodyLimitMiddleware(next, cfg, NullLogger<RequestBodyLimitMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "POST";
        if (contentLength.HasValue)
        {
            ctx.Request.ContentLength = contentLength.Value;
        }
        ctx.Request.Body = new MemoryStream(body);
        ctx.Response.Body = new MemoryStream();
        return (middleware, ctx);
    }

    [Fact]
    public async Task ContentLength_OverLimit_RejectsBeforeNext()
    {
        var (middleware, ctx) = Arrange(maxBytes: 16, contentLength: 1024, body: Array.Empty<byte>());

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ContentLength_UnderLimit_PassesThrough()
    {
        var (middleware, ctx) = Arrange(maxBytes: 1024, contentLength: 8, body: new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var called = false;
        // Override middleware with a recorder.
        var recorder = new RequestBodyLimitMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            new WebApiConfig { MaxRequestBodyBytes = 1024 },
            NullLogger<RequestBodyLimitMiddleware>.Instance);

        await recorder.InvokeAsync(ctx);

        Assert.True(called);
        Assert.NotEqual(StatusCodes.Status413PayloadTooLarge, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ChunkedBody_ExceedsLimit_Returns413()
    {
        // ContentLength is null (chunked transfer).
        var payload = new byte[64];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i + 1);
        var (middleware, ctx) = Arrange(maxBytes: 8, contentLength: null, body: payload);

        // Replace the no-op next with a reader that actually consumes the body.
        // The middleware wraps ctx.Request.Body in a LimitedStream; reading from
        // it past the cap should throw InvalidDataException, which the middleware
        // converts into a 413 response.
        var reader = new RequestBodyLimitMiddleware(
            async c =>
            {
                var buf = new byte[64];
                _ = await c.Request.Body.ReadAsync(buf);
            },
            new WebApiConfig { MaxRequestBodyBytes = 8 },
            NullLogger<RequestBodyLimitMiddleware>.Instance);

        await reader.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NonApiPath_BypassesMiddleware()
    {
        var (middleware, ctx) = Arrange(maxBytes: 16, contentLength: 1024, body: Array.Empty<byte>(), path: "/");
        var called = false;
        var recorder = new RequestBodyLimitMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            new WebApiConfig { MaxRequestBodyBytes = 16 },
            NullLogger<RequestBodyLimitMiddleware>.Instance);
        var c2 = new DefaultHttpContext();
        c2.Request.Path = "/";
        c2.Request.Method = "GET";
        c2.Request.ContentLength = 1024;
        c2.Request.Body = new MemoryStream(Array.Empty<byte>());
        c2.Response.Body = new MemoryStream();

        await recorder.InvokeAsync(c2);

        Assert.True(called);
    }
}
