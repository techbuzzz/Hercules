using System.Net;
using System.Net.Sockets;
using Hercules.Config;

namespace Hercules.Skills.Marketplace;

/// <summary>
///     R1/R2: SSRF and unbounded-download guard for <c>POST /api/marketplace/import-url</c>.
/// </summary>
/// <remarks>
///     Previously the endpoint accepted any URL a caller with a <c>contribute</c> key
///     supplied and fetched it server-side, with no scheme, host or address validation.
///     The <c>Marketplace:AllowHttpImport</c> flag existed in configuration but was never read,
///     so the documented guard did not exist. That let an authenticated caller reach
///     loopback services and cloud metadata endpoints (for example
///     <c>http://169.254.169.254/</c>).
///
///     Layers applied here:
///     <list type="number">
///         <item>the feature must be explicitly enabled (<c>AllowHttpImport</c>);</item>
///         <item>scheme must be <c>https</c> unless <c>AllowInsecureHttpImport</c> is set;</item>
///         <item>the host must not be a literal private/loopback/link-local address;</item>
///         <item>DNS must resolve to public addresses only — checked on every hop, which
///               is what defeats DNS rebinding between validation and connection;</item>
///         <item>redirects are followed manually so each hop is re-validated;</item>
///         <item>the body is streamed with a hard byte cap, so an oversized response is
///               aborted instead of fully buffered.</item>
///     </list>
/// </remarks>
public static class SkillImportUrlGuard
{
    private const int MaxRedirects = 5;

    /// <summary>Addresses that must never be reachable from a user-supplied import URL.</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6None)) return true;
        if (address.IsIPv4MappedToIPv6) return IsBlockedAddress(address.MapToIPv4());

        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
            {
                var b = address.GetAddressBytes();
                // 0.0.0.0/8, 10/8, 100.64/10 (CGNAT), 127/8, 169.254/16 (link-local,
                // includes cloud metadata), 172.16/12, 192.0.0/24, 192.168/16, 198.18/15.
                if (b[0] == 0) return true;
                if (b[0] == 10) return true;
                if (b[0] == 127) return true;
                if (b[0] == 169 && b[1] == 254) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 192 && b[1] == 0 && b[2] == 0) return true;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
                if (b[0] == 198 && (b[1] is 18 or 19)) return true;
                if (b[0] >= 224) return true; // multicast + reserved
                return false;
            }
            case AddressFamily.InterNetworkV6:
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
                if (address.IsIPv6Multicast) return true;
                // Unique local fc00::/7 and IPv4-compatible/transition ranges.
                var b = address.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return true;
                return false;
            }
            default:
                return true; // unknown family: fail closed
        }
    }

    /// <summary>
    ///     Validates scheme and host shape, then confirms every address the host resolves
    ///     to is publicly routable.
    /// </summary>
    public static async Task EnsureAllowedAsync(Uri uri, MarketplaceConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(config);

        if (!config.AllowHttpImport)
        {
            throw new InvalidOperationException(
                "Importing skills from a URL is disabled. Set Marketplace:AllowHttpImport=true to enable it.");
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"Unsupported URL scheme '{uri.Scheme}'. Only http and https are allowed.");
        }

        if (scheme == "http" && !config.AllowInsecureHttpImport)
        {
            throw new InvalidOperationException(
                "Plain http imports are disabled. Use https, or set Marketplace:AllowInsecureHttpImport=true.");
        }

        if (uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("URLs with embedded credentials are not allowed.");
        }

        // Optional exact-host allow-list, so an operator can pin a private registry
        // without weakening the private-address rules globally. Checked BEFORE the
        // literal-IP fast path, otherwise it silently never applied to literal hosts.
        if (config.AllowedImportHosts.Count > 0 &&
            !config.AllowedImportHosts.Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Host '{uri.Host}' is not in Marketplace:AllowedImportHosts.");
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            if (IsBlockedAddress(literal))
            {
                throw new InvalidOperationException($"URL host '{uri.Host}' resolves to a blocked address.");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("URL host is required.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not resolve host '{uri.Host}'.", ex);
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Host '{uri.Host}' did not resolve to any address.");
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                // Named for the operator: the hostname resolves to an internal address,
                // which is the classic SSRF pivot (and the DNS-rebinding case).
                throw new InvalidOperationException(
                    $"Host '{uri.Host}' resolves to a blocked internal address ({address}).");
            }
        }
    }

    /// <summary>
    ///     Downloads at most <paramref name="maxBytes"/>, aborting as soon as the limit is
    ///     exceeded. <c>Content-Length</c> is checked before any body byte is read, and the
    ///     stream is additionally capped so a lying or absent Content-Length cannot force
    ///     an unbounded buffer (R2).
    /// </summary>
    public static async Task<byte[]> DownloadWithLimitAsync(
        HttpClient client,
        Uri startUri,
        long maxBytes,
        CancellationToken ct)
    {
        var current = startUri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    throw new InvalidOperationException($"Redirect from '{current}' did not include a Location header.");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);

                // Re-validate every hop: a redirect is the cheapest way to walk from a
                // public host to 169.254.169.254 after the initial check passed.
                await EnsureAllowedForRedirectAsync(next, ct).ConfigureAwait(false);

                current = next;
                continue;
            }

            response.EnsureSuccessStatusCode();

            var declared = response.Content.Headers.ContentLength;
            if (declared is > 0 && declared > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Package exceeds the maximum size of {maxBytes / (1024 * 1024)} MB.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();

            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"Package exceeds the maximum size of {maxBytes / (1024 * 1024)} MB.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        throw new InvalidOperationException($"Too many redirects while downloading '{startUri}'.");
    }

    private static bool IsRedirect(System.Net.HttpStatusCode status) => status is
        System.Net.HttpStatusCode.MovedPermanently or
        System.Net.HttpStatusCode.Found or
        System.Net.HttpStatusCode.SeeOther or
        System.Net.HttpStatusCode.TemporaryRedirect or
        System.Net.HttpStatusCode.PermanentRedirect;

    /// <summary>
    ///     Redirect targets are re-checked without re-reading the feature flag, so a
    ///     previously authorised import cannot be bounced to an internal address.
    /// </summary>
    private static async Task EnsureAllowedForRedirectAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"Redirect to unsupported scheme '{uri.Scheme}' is not allowed.");
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            if (IsBlockedAddress(literal))
            {
                throw new InvalidOperationException($"Redirect target '{uri.Host}' resolves to a blocked address.");
            }
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not resolve redirect host '{uri.Host}'.", ex);
        }

        if (addresses.Length == 0 || addresses.Any(IsBlockedAddress))
        {
            throw new InvalidOperationException($"Redirect target '{uri.Host}' resolves to a blocked internal address.");
        }
    }
}