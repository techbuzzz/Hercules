namespace Hercules.Mesh.Discovery;

/// <summary>
///     Low-level mDNS/DNS-SD client.
///     Used by <see cref="MdnsDiscoverySource"/> to discover agents on the local network.
/// </summary>
public interface IMdnsClient : IDisposable
{
    /// <summary>
    ///     Discover agents advertising the Hercules mesh service via mDNS.
    ///     Returns a stream of discovered service instances.
    /// </summary>
    /// <param name="serviceType">
    ///     Service type to browse, e.g. "_hercules._tcp".
    ///     Null uses the default Hercules service type.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of discovered service instances.</returns>
    IAsyncEnumerable<MdnsServiceInstance> BrowseAsync(string? serviceType = null, CancellationToken ct = default);

    /// <summary>Resolve the full TXT and SRV records for a discovered instance.</summary>
    Task<MdnsResolvedInstance?> ResolveAsync(MdnsServiceInstance instance, CancellationToken ct = default);
}

/// <summary>
///     A mDNS service instance discovered on the local network.
/// </summary>
public sealed class MdnsServiceInstance
{
    /// <summary>Service name, e.g. "hercules-peer-1._hercules._tcp.local".</summary>
    public required string Name { get; init; }

    /// <summary>Service type, e.g. "_hercules._tcp.local".</summary>
    public required string ServiceType { get; init; }

    /// <summary>Hostname of the instance (without domain).</summary>
    public string HostName { get; init; } = "";

    /// <summary>Port number for the service.</summary>
    public int Port { get; init; }

    /// <summary>TXT record key-value pairs (if any).</summary>
    public Dictionary<string, string> TxtRecords { get; init; } = new();
}

/// <summary>
///     A fully resolved mDNS service instance with SRV record data.
/// </summary>
public sealed class MdnsResolvedInstance
{
    /// <summary>Instance name.</summary>
    public required string Name { get; init; }

    /// <summary>Target hostname (A/AAAA resolved).</summary>
    public required string TargetHost { get; init; }

    /// <summary>Port.</summary>
    public int Port { get; init; }

    /// <summary>Resolved endpoint URL (http/https + host + port).</summary>
    public required string Endpoint { get; init; }

    /// <summary>TXT record metadata.</summary>
    public Dictionary<string, string> TxtRecords { get; init; } = new();
}
