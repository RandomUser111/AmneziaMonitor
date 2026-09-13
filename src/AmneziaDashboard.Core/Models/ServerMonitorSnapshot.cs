namespace AmneziaDashboard.Core.Models;

public class ServerMonitorSnapshot
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string CpuUsage { get; set; } = "—";

    public string MemoryUsage { get; set; } = "—";

    public string DiskUsage { get; set; } = "—";

    public string Uptime { get; set; } = "—";

    public string NetworkInterface { get; set; } = string.Empty;

    public long NetworkReceivedBytes { get; set; }

    public long NetworkSentBytes { get; set; }

    public List<AmneziaContainerInfo> AmneziaContainers { get; set; } = [];

    public List<VpnPeerInfo> Peers { get; set; } = [];
}
