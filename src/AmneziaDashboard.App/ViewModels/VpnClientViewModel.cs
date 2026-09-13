using System;
using Avalonia.Media;
using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.App.ViewModels;

public sealed class VpnClientViewModel
{
    private static readonly IBrush OnlineBrush =
        new SolidColorBrush(Color.Parse("#22C55E"));

    private static readonly IBrush OfflineBrush =
        new SolidColorBrush(Color.Parse("#94A3B8"));

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

        var handshakeAge = handshake.HasValue ? now - handshake.Value : TimeSpan.MaxValue;
        IsOnline = handshake.HasValue && handshakeAge <= TimeSpan.FromMinutes(3);
        StatusBrush = IsOnline ? OnlineBrush : OfflineBrush;
        LastHandshake = FormatHandshake(handshakeAge, handshake.HasValue);

        // В WireGuard transfer-rx — байты, полученные сервером от peer,
        // transfer-tx — байты, отправленные сервером peer.
        DownloadedBytes = peer.SentBytes;
        UploadedBytes = peer.ReceivedBytes;
        Downloaded = FormatBytes(DownloadedBytes);
        Uploaded = FormatBytes(UploadedBytes);

        if (previousPeer is not null && elapsedSeconds > 0)
        {
            var downloadDelta = Math.Max(0, peer.SentBytes - previousPeer.SentBytes);
            var uploadDelta = Math.Max(0, peer.ReceivedBytes - previousPeer.ReceivedBytes);

            DownloadSpeed = FormatRate(downloadDelta / elapsedSeconds);
            UploadSpeed = FormatRate(uploadDelta / elapsedSeconds);
        }
        else
        {
            DownloadSpeed = "—";
            UploadSpeed = "—";
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
    public string LastHandshake { get; }
    public long DownloadedBytes { get; }
    public long UploadedBytes { get; }
    public string Downloaded { get; }
    public string Uploaded { get; }
    public string DownloadSpeed { get; }
    public string UploadSpeed { get; }

    public string StatusText => IsOnline ? "Онлайн" : "Офлайн";

    private static string FormatHandshake(TimeSpan age, bool hasHandshake)
    {
        if (!hasHandshake)
            return "Никогда";

        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age.TotalSeconds < 60)
            return $"{Math.Max(1, (int)age.TotalSeconds)} сек назад";

        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes} мин назад";

        if (age.TotalHours < 24)
            return $"{(int)age.TotalHours} ч назад";

        return $"{(int)age.TotalDays} дн назад";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} Б";

        var value = (double)bytes;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 0)
            bytesPerSecond = 0;

        var value = bytesPerSecond;
        string[] units = ["Б/с", "КБ/с", "МБ/с", "ГБ/с"];
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
            return $"{value:0} {units[unit]}";

        return $"{value:0.#} {units[unit]}";
    }

    private static string ShortKey(string key)
    {
        if (key.Length <= 14)
            return key;

        return $"{key[..7]}…{key[^6..]}";
    }
}
