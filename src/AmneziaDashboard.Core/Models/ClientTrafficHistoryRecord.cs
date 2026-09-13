namespace AmneziaDashboard.Core.Models;

public sealed class ClientTrafficHistoryRecord
{
    public long Id { get; set; }

    public string ServerKey { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ClientKey { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string ProtocolName { get; set; } = string.Empty;

    public string AllowedIps { get; set; } = string.Empty;

    public long DownloadTotalBytes { get; set; }

    public long UploadTotalBytes { get; set; }

    public long DownloadDeltaBytes { get; set; }

    public long UploadDeltaBytes { get; set; }
}
