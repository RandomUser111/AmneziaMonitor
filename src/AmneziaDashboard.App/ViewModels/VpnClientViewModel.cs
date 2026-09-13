using System;
using Avalonia.Media;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;

namespace AmneziaDashboard.App.ViewModels;

public sealed class VpnClientViewModel : ViewModelBase
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    private readonly TimeSpan _handshakeAge;
    private readonly bool _hasHandshake;
    private readonly double? _downloadBytesPerSecond;
    private readonly double? _uploadBytesPerSecond;

    public VpnClientViewModel(
        VpnPeerInfo peer,
        VpnPeerInfo? previousPeer,
        double elapsedSeconds,
        DateTimeOffset now)
    {
        ClientId = peer.ClientId;
        ContainerName = peer.ContainerName;
        Name = string.IsNullOrWhiteSpace(peer.ClientName) ? ShortKey(peer.ClientId) : peer.ClientName;
        Protocol = peer.ProtocolName;
        Address = string.IsNullOrWhiteSpace(peer.AllowedIps) ? "—" : peer.AllowedIps;
        Endpoint = string.IsNullOrWhiteSpace(peer.Endpoint) ? "—" : peer.Endpoint;

        var handshake = peer.LatestHandshakeUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(peer.LatestHandshakeUnix)
            : (DateTimeOffset?)null;

        _handshakeAge = handshake.HasValue ? now - handshake.Value : TimeSpan.MaxValue;
        _hasHandshake = handshake.HasValue;
        IsOnline = _hasHandshake && _handshakeAge <= TimeSpan.FromMinutes(3);
        StatusBrush = IsOnline ? OnlineBrush : OfflineBrush;

        // WireGuard transfer-rx is data received by the server from the peer;
        // transfer-tx is data sent by the server to the peer.
        DownloadedBytes = peer.SentBytes;
        UploadedBytes = peer.ReceivedBytes;

        if (previousPeer is not null && elapsedSeconds > 0)
        {
            var downloadDelta = Math.Max(0, peer.SentBytes - previousPeer.SentBytes);
            var uploadDelta = Math.Max(0, peer.ReceivedBytes - previousPeer.ReceivedBytes);
            _downloadBytesPerSecond = downloadDelta / elapsedSeconds;
            _uploadBytesPerSecond = uploadDelta / elapsedSeconds;
        }
    }

    public string ClientId { get; }
    public string ContainerName { get; }
    public string Name { get; }
    public string Protocol { get; }
    public string Address { get; }
    public string Endpoint { get; }
    public bool IsOnline { get; }
    public IBrush StatusBrush { get; }
    public long DownloadedBytes { get; }
    public long UploadedBytes { get; }

    public string LastHandshake => FormatHandshake(_handshakeAge, _hasHandshake);
    public string Downloaded => FormatBytes(DownloadedBytes);
    public string Uploaded => FormatBytes(UploadedBytes);
    public string DownloadSpeed => _downloadBytesPerSecond.HasValue ? FormatRate(_downloadBytesPerSecond.Value) : "—";
    public string UploadSpeed => _uploadBytesPerSecond.HasValue ? FormatRate(_uploadBytesPerSecond.Value) : "—";
    public string StatusText => IsOnline ? LocalizationService.T("Online", "Онлайн") : LocalizationService.T("Offline", "Офлайн");

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastHandshake));
        OnPropertyChanged(nameof(Downloaded));
        OnPropertyChanged(nameof(Uploaded));
        OnPropertyChanged(nameof(DownloadSpeed));
        OnPropertyChanged(nameof(UploadSpeed));
    }

    private static string FormatHandshake(TimeSpan age, bool hasHandshake)
    {
        if (!hasHandshake)
            return LocalizationService.T("Never", "Никогда");

        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age.TotalSeconds < 60)
            return LocalizationService.IsRussian ? $"{Math.Max(1, (int)age.TotalSeconds)} сек назад" : $"{Math.Max(1, (int)age.TotalSeconds)} sec ago";
        if (age.TotalMinutes < 60)
            return LocalizationService.IsRussian ? $"{(int)age.TotalMinutes} мин назад" : $"{(int)age.TotalMinutes} min ago";
        if (age.TotalHours < 24)
            return LocalizationService.IsRussian ? $"{(int)age.TotalHours} ч назад" : $"{(int)age.TotalHours} h ago";

        return LocalizationService.IsRussian ? $"{(int)age.TotalDays} дн назад" : $"{(int)age.TotalDays} d ago";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} {LocalizationService.T("B", "Б")}";

        var value = (double)bytes;
        string[] units = LocalizationService.IsRussian ? ["Б", "КБ", "МБ", "ГБ", "ТБ"] : ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatRate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        string[] units = LocalizationService.IsRussian ? ["Б/с", "КБ/с", "МБ/с", "ГБ/с"] : ["B/s", "KB/s", "MB/s", "GB/s"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private static string ShortKey(string key) => key.Length <= 14 ? key : $"{key[..7]}…{key[^6..]}";
}
