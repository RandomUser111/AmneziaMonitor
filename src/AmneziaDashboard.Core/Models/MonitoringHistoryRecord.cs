namespace AmneziaDashboard.Core.Models;

public class MonitoringHistoryRecord
{
    public long Id { get; set; }

    public string ServerKey { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public double? CpuPercent { get; set; }

    public double? RamPercent { get; set; }

    public double? DiskPercent { get; set; }

    public double DownloadBytesPerSecond { get; set; }

    public double UploadBytesPerSecond { get; set; }

    public int ClientsOnline { get; set; }

    public int ClientsTotal { get; set; }

    public long TrafficReceivedBytes { get; set; }

    public long TrafficSentBytes { get; set; }
}
