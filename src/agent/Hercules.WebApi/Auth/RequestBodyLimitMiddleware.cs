using Hercules.WebApi.Config;

namespace Hercules.WebApi.Auth;

/// <summary>
///     Defense-in-depth ограничение размера тела запроса поверх Kestrel
///     <c>MaxRequestBodySize</c> (task_081). Kestrel сам по себе не отдаёт тело,
///     превышающее лимит, но поток всё равно создаётся, и при chunked transfer
///     (<c>Content-Length</c> не задан) проверка «на входе» не помогает.
///     <para>
///         Middleware оборачивает <see cref="HttpRequest.Body" /> в
///     <see cref="LimitedStream" />, который бросает <see cref="InvalidDataException" />
///     при превышении лимита. Это даёт чистый 413 даже для chunked uploads.
///     </para>
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, WebApiConfig cfg, ILogger<RequestBodyLimitMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (cfg.MaxRequestBodyBytes <= 0 || !path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        // Cheap upfront check on Content-Length (most clients send it).
        if (context.Request.ContentLength is long cl && cl > cfg.MaxRequestBodyBytes)
        {
            logger.LogWarning(
                "[RequestBodyLimit] Rejecting {Method} {Path}: Content-Length {Bytes} > {Limit}",
                context.Request.Method, path, cl, cfg.MaxRequestBodyBytes);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "Request body too large." });
            return;
        }

        // For chunked uploads ContentLength is null — wrap the stream.
        var original = context.Request.Body;
        if (original is not LimitedStream)
        {
            context.Request.Body = new LimitedStream(original, cfg.MaxRequestBodyBytes, logger, path, context.Request.Method);
        }

        try
        {
            await next(context);
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("Request body exceeded", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "[RequestBodyLimit] Rejecting {Method} {Path}: stream exceeded {Limit} bytes",
                context.Request.Method, path, cfg.MaxRequestBodyBytes);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new { error = "Request body too large." });
            }
        }
        finally
        {
            // Restore the original body so any subsequent middleware / re-read sees
            // a clean, non-limited stream. The wrapper is a forward-only view, so it
            // can be discarded safely.
            context.Request.Body = original;
        }
    }
}

/// <summary>
///     Read-only, forward-only stream wrapper that throws when more than
///     <c>maxBytes</c> are read from the underlying stream. Detects over-limit
///     uploads during the read loop instead of relying solely on
///     <c>Content-Length</c>.
/// </summary>
public sealed class LimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly ILogger _logger;
    private readonly string _path;
    private readonly string _method;
    private long _readBytes;

    public LimitedStream(Stream inner, long maxBytes, ILogger logger, string path, string method)
    {
        _inner = inner;
        _maxBytes = maxBytes;
        _logger = logger;
        _path = path;
        _method = method;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _readBytes;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = _inner.Read(buffer, offset + total, count - total);
            if (n <= 0)
            {
                break;
            }
            total += n;
            _readBytes += n;
            if (_readBytes > _maxBytes)
            {
                _logger.LogWarning(
                    "[LimitedStream] {Method} {Path} exceeded {Limit} bytes",
                    _method, _path, _maxBytes);
                throw new InvalidDataException(
                    $"Request body exceeded limit of {_maxBytes} bytes.");
            }
        }
        return total;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            _readBytes += n;
            if (_readBytes > _maxBytes)
            {
                _logger.LogWarning(
                    "[LimitedStream] {Method} {Path} exceeded {Limit} bytes",
                    _method, _path, _maxBytes);
                throw new InvalidDataException(
                    $"Request body exceeded limit of {_maxBytes} bytes.");
            }
        }
        return n;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
