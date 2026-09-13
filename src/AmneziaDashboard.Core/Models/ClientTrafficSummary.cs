namespace AmneziaDashboard.Core.Models;

public sealed class ClientTrafficSummary
{
    public string ClientKey { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string ProtocolName { get; set; } = string.Empty;

    public string AllowedIps { get; set; } = string.Empty;

    public long DownloadBytes { get; set; }

    public long UploadBytes { get; set; }

    public int SampleCount { get; set; }

    public DateTimeOffset FirstSampleAt { get; set; }

    public DateTimeOffset LastSampleAt { get; set; }

    public long TotalBytes => DownloadBytes + UploadBytes;
}
