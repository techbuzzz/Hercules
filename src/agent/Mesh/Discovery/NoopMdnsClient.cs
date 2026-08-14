using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Discovery;

/// <summary>
///     Stub mDNS client for platforms that do not support DNS-SD multicast
///     (e.g. Windows without the mDNSResponder component).
///     Returns an empty stream and logs that mDNS is unavailable.
///     Real mDNS support can be added via a platform-specific implementation
///     (e.g. using the Bonjour SDK on macOS, or embedding mDNSResponder on Linux).
/// </summary>
public sealed class NoopMdnsClient : IMdnsClient
{
    private readonly ILogger<NoopMdnsClient> _logger;

    public NoopMdnsClient(ILogger<NoopMdnsClient> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<MdnsServiceInstance> BrowseAsync(
        string? serviceType = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation(
            "mDNS discovery is not available on this platform. "
            + "Configure static peers or use the capability registry for peer discovery.");
        await Task.CompletedTask;
        yield break;
    }

    public Task<MdnsResolvedInstance?> ResolveAsync(
        MdnsServiceInstance instance,
        CancellationToken ct = default)
    {
        return Task.FromResult<MdnsResolvedInstance?>(null);
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
