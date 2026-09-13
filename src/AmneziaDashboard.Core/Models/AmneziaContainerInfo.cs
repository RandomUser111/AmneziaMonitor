namespace AmneziaDashboard.Core.Models;

public class AmneziaContainerInfo
{
    public string ContainerName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ProtocolKey { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Ports { get; set; } = string.Empty;

    public bool IsRunning { get; set; }

    public int? ClientCount { get; set; }
}
