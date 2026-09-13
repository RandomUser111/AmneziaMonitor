namespace AmneziaDashboard.Core.Models;

public class VpnPeerInfo
{
    public string ContainerName { get; set; } = string.Empty;

    public string ProtocolName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string AllowedIps { get; set; } = string.Empty;

    public long LatestHandshakeUnix { get; set; }

    public long ReceivedBytes { get; set; }

    public long SentBytes { get; set; }
}
