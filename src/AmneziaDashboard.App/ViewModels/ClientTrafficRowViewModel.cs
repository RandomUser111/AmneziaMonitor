using AmneziaDashboard.App.Services;
using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.App.ViewModels;

public sealed class ClientTrafficRowViewModel : ViewModelBase
{
    public ClientTrafficRowViewModel(ClientTrafficSummary summary, long allClientsBytes)
    {
        ClientKey = summary.ClientKey;
        ClientId = summary.ClientId;
        Name = string.IsNullOrWhiteSpace(summary.ClientName)
            ? FallbackName(summary)
            : summary.ClientName;
        ContainerName = summary.ContainerName;
        Protocol = string.IsNullOrWhiteSpace(summary.ProtocolName) ? "—" : summary.ProtocolName;
        Address = string.IsNullOrWhiteSpace(summary.AllowedIps) ? "—" : summary.AllowedIps;
        DownloadBytes = Math.Max(0, summary.DownloadBytes);
        UploadBytes = Math.Max(0, summary.UploadBytes);
        SampleCount = summary.SampleCount;
        FirstSampleAt = summary.FirstSampleAt;
        LastSampleAt = summary.LastSampleAt;
        Share = allClientsBytes > 0 ? TotalBytes * 100d / allClientsBytes : 0d;
    }

    public string ClientKey { get; }
    public string ClientId { get; }
    public string Name { get; }
    public string ContainerName { get; }
    public string Protocol { get; }
    public string Address { get; }
    public long DownloadBytes { get; }
    public long UploadBytes { get; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    public int SampleCount { get; }
    public DateTimeOffset FirstSampleAt { get; }
    public DateTimeOffset LastSampleAt { get; }
    public double Share { get; }

    public string DownloadText => FormatBytes(DownloadBytes);
    public string UploadText => FormatBytes(UploadBytes);
    public string TotalText => FormatBytes(TotalBytes);
    public string ShareText => $"{Share:0.#}%";

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(DownloadText));
        OnPropertyChanged(nameof(UploadText));
        OnPropertyChanged(nameof(TotalText));
    }

    private static string FallbackName(ClientTrafficSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.AllowedIps))
            return summary.AllowedIps;

        var key = summary.ClientId;
        if (string.IsNullOrWhiteSpace(key))
            return LocalizationService.T("Unknown client", "Неизвестный клиент");

        return key.Length <= 14 ? key : $"{key[..7]}…{key[^6..]}";
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, (double)bytes);
        string[] units = LocalizationService.IsRussian
            ? ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"]
            : ["B", "KB", "MB", "GB", "TB", "PB"];

        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
