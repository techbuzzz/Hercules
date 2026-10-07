using System.Net;
using Hercules.Config;
using Hercules.Skills.Marketplace;
using Xunit;

namespace Hercules.Agent.Tests.WebApi;

/// <summary>
///     R1/R2 regression tests for the marketplace URL import guard.
/// </summary>
/// <remarks>
///     Before this guard, <c>POST /api/marketplace/import-url</c> fetched any URL a
///     <c>contribute</c>-key caller supplied — the <c>Marketplace:AllowHttpImport</c> flag
///     existed in config but was never read — and buffered the whole response before
///     applying a size limit that therefore could not hold.
/// </remarks>
public class MarketplaceUrlImportSecurityTests
{
    private static MarketplaceConfig Enabled(bool insecureHttp = false, params string[] hosts) => new()
    {
        AllowHttpImport = true,
        AllowInsecureHttpImport = insecureHttp,
        AllowedImportHosts = [.. hosts],
    };

    // ---------------------------------------------------------------------
    // Feature flag
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EnsureAllowedAsync_WhenFlagDisabled_Rejects()
    {
        var cfg = new MarketplaceConfig { AllowHttpImport = false };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(new Uri("https://example.com/pkg.skillpkg"), cfg, default));

        Assert.Contains("AllowHttpImport", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Scheme
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/pkg.skillpkg")]
    [InlineData("gopher://example.com:70/x")]
    public async Task EnsureAllowedAsync_NonHttpScheme_Rejects(string url)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(new Uri(url), Enabled(), default));

        Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAllowedAsync_PlainHttp_RejectedUnlessSecondOptIn()
    {
        // http://93.184.216.34 is a literal address, so no DNS lookup is needed.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(
                new Uri("http://93.184.216.34/pkg.skillpkg"), Enabled(), default));
        Assert.Contains("http", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAllowedAsync_PlainHttp_AllowedWithInsecureOptIn()
    {
        await SkillImportUrlGuard.EnsureAllowedAsync(
            new Uri("http://93.184.216.34/pkg.skillpkg"), Enabled(insecureHttp: true), default);
    }

    [Fact]
    public async Task EnsureAllowedAsync_EmbeddedCredentials_Rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(
                new Uri("https://user:pass@example.com/pkg.skillpkg"), Enabled(), default));
    }

    // ---------------------------------------------------------------------
    // SSRF address matrix
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("http://127.0.0.1/pkg.skillpkg")]              // loopback
    [InlineData("http://127.1.2.3/pkg.skillpkg")]              // whole 127/8
    [InlineData("http://169.254.169.254/latest/meta-data/")]   // cloud metadata
    [InlineData("http://10.0.0.1/pkg.skillpkg")]               // RFC1918
    [InlineData("http://192.168.1.1/pkg.skillpkg")]            // RFC1918
    [InlineData("http://172.16.5.4/pkg.skillpkg")]             // RFC1918
    [InlineData("http://100.64.0.1/pkg.skillpkg")]            // CGNAT
    [InlineData("http://0.0.0.0/pkg.skillpkg")]                // unspecified
    [InlineData("http://[::1]/pkg.skillpkg")]                  // IPv6 loopback
    [InlineData("http://[fd00::1]/pkg.skillpkg")]               // IPv6 ULA
    [InlineData("http://[fe80::1]/pkg.skillpkg")]              // IPv6 link-local
    public async Task EnsureAllowedAsync_InternalAddress_Rejected(string url)
    {
        var cfg = Enabled(insecureHttp: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(new Uri(url), cfg, default));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("10.255.255.254", true)]
    [InlineData("::1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("8.8.8.8", false)]
    public void IsBlockedAddress_Matrix(string ip, bool blocked)
    {
        Assert.Equal(blocked, SkillImportUrlGuard.IsBlockedAddress(IPAddress.Parse(ip)));
    }

    // ---------------------------------------------------------------------
    // Host allow-list
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EnsureAllowedAsync_HostNotInAllowList_Rejected()
    {
        var cfg = Enabled(hosts: ["registry.internal.example"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SkillImportUrlGuard.EnsureAllowedAsync(
                new Uri("https://93.184.216.34/pkg.skillpkg"), cfg, default));
    }

    [Fact]
    public async Task EnsureAllowedAsync_HostInAllowList_Accepted()
    {
        var cfg = Enabled(hosts: ["93.184.216.34"]);

        await SkillImportUrlGuard.EnsureAllowedAsync(
            new Uri("https://93.184.216.34/pkg.skillpkg"), cfg, default);
    }

    // ---------------------------------------------------------------------
    // R2: bounded download
    // ---------------------------------------------------------------------

    private sealed class ContentLengthOnlyHandler(long declaredLength) : HttpMessageHandler
    {
        public bool BodyWasRead { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(GuardStream());
            content.Headers.ContentLength = declaredLength;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }

        private Stream GuardStream() => new ThrowingStream(() => BodyWasRead = true);
    }

    private sealed class ThrowingStream(Action onRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            throw new InvalidOperationException("Body must not be read when Content-Length already exceeds the cap.");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            onRead();
            throw new InvalidOperationException("Body must not be read when Content-Length already exceeds the cap.");
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task DownloadWithLimitAsync_OversizedContentLength_AbortsBeforeReadingBody()
    {
        // R2: the old code called GetByteArrayAsync and only compared bytes.Length after the
        // whole response had been buffered, so an attacker-controlled size was never a real
        // limit. Declared length above the cap must fail without touching the body.
        var handler = new ContentLengthOnlyHandler(declaredLength: 10L * 1024 * 1024 * 1024);
        using var client = new HttpClient(handler);
        const long maxBytes = 50 * 1024 * 1024;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SkillImportUrlGuard
            .DownloadWithLimitAsync(client, new Uri("https://93.184.216.34/pkg.skillpkg"), maxBytes, default));

        Assert.Contains("maximum size", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handler.BodyWasRead, "Body must not be read once Content-Length exceeds the cap.");
    }

    private sealed class EndlessStream(long chunkSize) : Stream
    {
        public long BytesServed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(Math.Min(chunkSize, count), 81920);
            Array.Fill(buffer, (byte)'A', offset, n);
            BytesServed += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class EndlessHandler(EndlessStream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // No Content-Length: the cap must still be enforced while streaming.
            var content = new StreamContent(stream);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task DownloadWithLimitAsync_NoContentLength_StopsStreamingAtCap()
    {
        const long maxBytes = 1 * 1024 * 1024;
        var stream = new EndlessStream(chunkSize: 81920);
        using var client = new HttpClient(new EndlessHandler(stream));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SkillImportUrlGuard
            .DownloadWithLimitAsync(client, new Uri("https://93.184.216.34/pkg.skillpkg"), maxBytes, default));

        // Proves the download was aborted at the cap instead of running unbounded.
        Assert.InRange(stream.BytesServed, maxBytes, maxBytes + 81920);
    }

    [Fact]
    public async Task DownloadWithLimitAsync_RedirectToInternalAddress_Rejected()
    {
        var handler = new RedirectHandler(new Uri("http://169.254.169.254/latest/meta-data/"));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SkillImportUrlGuard
            .DownloadWithLimitAsync(client, new Uri("https://93.184.216.34/pkg.skillpkg"), 1024, default));
    }

    private sealed class RedirectHandler(Uri target) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Found)
            {
                Headers = { Location = target },
            });
    }
}