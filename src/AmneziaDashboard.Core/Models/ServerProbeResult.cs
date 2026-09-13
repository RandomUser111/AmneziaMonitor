namespace AmneziaDashboard.Core.Models;

public class ServerProbeResult
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    public string Kernel { get; set; } = string.Empty;

    public string Uptime { get; set; } = string.Empty;

    public string CpuUsage { get; set; } = "—";

    public string MemoryUsage { get; set; } = "—";

    public string DiskUsage { get; set; } = "—";

    public bool DockerInstalled { get; set; }

    public string DockerVersion { get; set; } = string.Empty;

    public List<AmneziaContainerInfo> AmneziaContainers { get; set; } = [];
}
