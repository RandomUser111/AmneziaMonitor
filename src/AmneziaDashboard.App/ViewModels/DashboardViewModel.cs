using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private static readonly IBrush OfflineBrush =
        new SolidColorBrush(Color.Parse("#94A3B8"));

    private static readonly IBrush OnlineBrush =
        new SolidColorBrush(Color.Parse("#22C55E"));

    private static readonly IBrush ErrorBrush =
        new SolidColorBrush(Color.Parse("#EF4444"));

    private readonly IServerMonitorService _monitorService;
    private readonly IMonitoringHistoryStore? _historyStore;
    private readonly AppEventLogService? _eventLog;
    private readonly DesktopNotificationService? _notificationService;
    private CancellationTokenSource? _monitorCancellation;
    private ServerMonitorSnapshot? _previousSnapshot;
    private DateTimeOffset? _lastHistorySave;
    private bool _historyCleanupDone;
    private readonly Dictionary<string, (long Downloaded, long Uploaded)> _lastSavedClientCounters = new(StringComparer.Ordinal);

    [ObservableProperty]
    private string _serverStatus = LocalizationService.T("Not connected", "Не подключён");

    [ObservableProperty]
    private IBrush _serverStatusBrush = OfflineBrush;

    [ObservableProperty]
    private string _serverAddress = LocalizationService.T("No server selected", "Сервер не выбран");

    [ObservableProperty]
    private string _sshStatus = "SSH: —";

    [ObservableProperty]
    private string _cpuUsage = "—";

    [ObservableProperty]
    private string _ramUsage = "—";

    [ObservableProperty]
    private string _diskUsage = "—";

    [ObservableProperty]
    private string _uptime = "—";

    [ObservableProperty]
    private string _downloadSpeed = "—";

    [ObservableProperty]
    private string _uploadSpeed = "—";

    [ObservableProperty]
    private string _clientsOnline = "—";

    [ObservableProperty]
    private string _clientsTotal = "—";

    [ObservableProperty]
    private string _trafficReceived = "—";

    [ObservableProperty]
    private string _trafficSent = "—";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _hasProtocols;

    [ObservableProperty]
    private bool _hasNoProtocols = true;

    [ObservableProperty]
    private string _protocolsMessage = LocalizationService.T("Connect to a server to discover Amnezia containers.", "Подключитесь к серверу для поиска контейнеров Amnezia.");

    [ObservableProperty]
    private string _connectButtonText = LocalizationService.T("Connect server", "Подключить сервер");

    [ObservableProperty]
    private string _dockerVersion = "—";

    [ObservableProperty]
    private string _operatingSystem = "—";

    [ObservableProperty]
    private string _monitorStatus = LocalizationService.T("Monitoring stopped", "Мониторинг остановлен");

    public ObservableCollection<ProtocolStatusViewModel> Protocols { get; } = [];

    public ObservableCollection<VpnClientViewModel> Clients { get; } = [];

    public ServerConnection? CurrentConnection { get; private set; }

    public DashboardViewModel(
        IServerMonitorService monitorService,
        IMonitoringHistoryStore? historyStore = null,
        AppEventLogService? eventLog = null,
        DesktopNotificationService? notificationService = null)
    {
        _monitorService = monitorService;
        _historyStore = historyStore;
        _eventLog = eventLog;
        _notificationService = notificationService;
        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
    }


    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        if (CurrentConnection is null)
        {
            ServerStatus = LocalizationService.T("Not connected", "Не подключён");
            ServerAddress = LocalizationService.T("No server selected", "Сервер не выбран");
            ConnectButtonText = LocalizationService.T("Connect server", "Подключить сервер");
            MonitorStatus = LocalizationService.T("Monitoring stopped", "Мониторинг остановлен");
            ProtocolsMessage = LocalizationService.T(
                "Connect to a server to discover Amnezia containers.",
                "Подключитесь к серверу для поиска контейнеров Amnezia.");
        }
        else
        {
            ServerStatus = IsConnected
                ? LocalizationService.T("Connected", "Подключён")
                : LocalizationService.T("No connection", "Нет связи");
            ConnectButtonText = LocalizationService.T("Switch server", "Сменить сервер");
            if (HasNoProtocols)
                ProtocolsMessage = LocalizationService.T("No Amnezia containers found.", "Контейнеры Amnezia не найдены.");
        }
    }

    public void ApplyConnection(
        ServerConnection connection,
        ServerProbeResult probe)
    {
        StopMonitoring();

        CurrentConnection = connection;
        IsConnected = true;
        ServerStatus = LocalizationService.T("Connected", "Подключён");
        ServerStatusBrush = OnlineBrush;
        ConnectButtonText = LocalizationService.T("Switch server", "Сменить сервер");
        MonitorStatus = LocalizationService.T("Starting monitoring…", "Запуск мониторинга…");

        ServerAddress = $"{connection.Name} · {probe.Hostname}";
        SshStatus = $"SSH: {connection.Host}:{connection.Port}";

        CpuUsage = ValueOrDash(probe.CpuUsage);
        RamUsage = ValueOrDash(probe.MemoryUsage);
        DiskUsage = ValueOrDash(probe.DiskUsage);
        Uptime = ValueOrDash(probe.Uptime);
        DockerVersion = ValueOrDash(probe.DockerVersion);
        OperatingSystem = ValueOrDash(probe.OperatingSystem);

        ApplyProtocols(probe.AmneziaContainers);

        var knownClientCounts = probe.AmneziaContainers
            .Where(x => x.ClientCount.HasValue)
            .Select(x => x.ClientCount!.Value)
            .ToList();

        ClientsTotal = knownClientCounts.Count > 0
            ? knownClientCounts.Sum().ToString()
            : "—";

        ClientsOnline = "—";
        TrafficReceived = "—";
        TrafficSent = "—";
        DownloadSpeed = "—";
        UploadSpeed = "—";
        Clients.Clear();
        _previousSnapshot = null;
        _lastHistorySave = null;
        _lastSavedClientCounters.Clear();

        _eventLog?.Success("SSH", LocalizationService.T($"Connected to {connection.Name} ({connection.Host}:{connection.Port}).", $"Подключено к {connection.Name} ({connection.Host}:{connection.Port})."));
        StartMonitoring();
    }

    public void StopMonitoring()
    {
        if (_monitorCancellation is null)
            return;

        _monitorCancellation.Cancel();
        _monitorCancellation.Dispose();
        _monitorCancellation = null;
    }

    private void StartMonitoring()
    {
        if (CurrentConnection is null)
            return;

        _monitorCancellation = new CancellationTokenSource();
        _ = MonitorLoopAsync(_monitorCancellation.Token);
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        // Первый снимок получаем сразу, затем обновляем каждые 5 секунд.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connection = CurrentConnection;
                if (connection is null)
                    return;

                var previousSnapshot = _previousSnapshot;
                var snapshot = await _monitorService.ReadAsync(connection, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() => ApplyMonitorSnapshot(snapshot));
                await SaveHistorySampleAsync(
                    connection,
                    snapshot,
                    previousSnapshot,
                    cancellationToken);

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplyMonitorError(ex.Message));

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void ApplyMonitorSnapshot(ServerMonitorSnapshot snapshot)
    {
        if (!snapshot.Success)
        {
            ApplyMonitorError(snapshot.ErrorMessage);
            return;
        }

        var wasConnected = IsConnected;
        IsConnected = true;
        if (!wasConnected)
        {
            var restoredMessage = LocalizationService.T("Server connection restored.", "Связь с сервером восстановлена.");
            _eventLog?.Success("Monitoring", restoredMessage);
            _ = _notificationService?.NotifyAsync(
                LocalizationService.T("Amnezia Monitor", "Amnezia Monitor"),
                restoredMessage,
                DesktopNotificationKind.Information);
        }
        ServerStatus = LocalizationService.T("Connected", "Подключён");
        ServerStatusBrush = OnlineBrush;
        MonitorStatus = LocalizationService.T($"Updated {DateTime.Now:HH:mm:ss}", $"Обновлено {DateTime.Now:HH:mm:ss}");

        CpuUsage = ValueOrDash(snapshot.CpuUsage);
        RamUsage = ValueOrDash(snapshot.MemoryUsage);
        DiskUsage = ValueOrDash(snapshot.DiskUsage);
        Uptime = ValueOrDash(snapshot.Uptime);

        var elapsedSeconds = _previousSnapshot is null
            ? 0
            : Math.Max(0.001, (snapshot.Timestamp - _previousSnapshot.Timestamp).TotalSeconds);

        if (_previousSnapshot is not null &&
            snapshot.NetworkReceivedBytes >= _previousSnapshot.NetworkReceivedBytes &&
            snapshot.NetworkSentBytes >= _previousSnapshot.NetworkSentBytes)
        {
            DownloadSpeed = FormatRate(
                (snapshot.NetworkReceivedBytes - _previousSnapshot.NetworkReceivedBytes) / elapsedSeconds);

            UploadSpeed = FormatRate(
                (snapshot.NetworkSentBytes - _previousSnapshot.NetworkSentBytes) / elapsedSeconds);
        }
        else
        {
            DownloadSpeed = "—";
            UploadSpeed = "—";
        }

        NotifyContainerStateChanges(_previousSnapshot, snapshot);
        ApplyProtocols(snapshot.AmneziaContainers);
        ApplyClients(snapshot, elapsedSeconds);

        _previousSnapshot = snapshot;
    }

    private void ApplyMonitorError(string message)
    {
        var wasConnected = IsConnected;
        IsConnected = false;
        if (wasConnected)
        {
            var lostMessage = string.IsNullOrWhiteSpace(message)
                ? LocalizationService.T("Server connection lost.", "Потеряна связь с сервером.")
                : LocalizationService.T($"Server connection lost: {LocalizationService.TranslateExternalMessage(message)}", $"Потеряна связь с сервером: {message}");
            _eventLog?.Warning("Monitoring", lostMessage);
            _ = _notificationService?.NotifyAsync(
                LocalizationService.T("Server disconnected", "Сервер отключён"),
                lostMessage,
                DesktopNotificationKind.Error);
        }
        ServerStatus = LocalizationService.T("No connection", "Нет связи");
        ServerStatusBrush = ErrorBrush;
        MonitorStatus = string.IsNullOrWhiteSpace(message)
            ? LocalizationService.T("Update error", "Ошибка обновления")
            : LocalizationService.T($"Error: {LocalizationService.TranslateExternalMessage(message)}", $"Ошибка: {message}");
    }

    private void NotifyContainerStateChanges(ServerMonitorSnapshot? previous, ServerMonitorSnapshot current)
    {
        if (previous is null)
            return;

        var oldByName = previous.AmneziaContainers.ToDictionary(x => x.ContainerName, StringComparer.OrdinalIgnoreCase);
        foreach (var container in current.AmneziaContainers)
        {
            if (!oldByName.TryGetValue(container.ContainerName, out var oldContainer) || oldContainer.IsRunning == container.IsRunning)
                continue;

            var message = container.IsRunning
                ? LocalizationService.T($"{container.DisplayName} started.", $"{container.DisplayName} запущен.")
                : LocalizationService.T($"{container.DisplayName} stopped unexpectedly.", $"{container.DisplayName} неожиданно остановлен.");

            if (container.IsRunning)
                _eventLog?.Success("Protocol", message);
            else
                _eventLog?.Warning("Protocol", message);

            _ = _notificationService?.NotifyAsync(
                LocalizationService.T("VPN protocol state changed", "Изменилось состояние VPN-протокола"),
                message,
                container.IsRunning ? DesktopNotificationKind.Information : DesktopNotificationKind.Error);
        }
    }

    private void ApplyProtocols(IEnumerable<AmneziaContainerInfo> containers)
    {
        var containerList = containers.ToList();

        Protocols.Clear();
        foreach (var container in containerList)
            Protocols.Add(new ProtocolStatusViewModel(container));

        HasProtocols = Protocols.Count > 0;
        HasNoProtocols = !HasProtocols;

        ProtocolsMessage = HasProtocols
            ? string.Empty
            : LocalizationService.T("No Amnezia containers found.", "Контейнеры Amnezia не найдены.");
    }

    private void ApplyClients(ServerMonitorSnapshot snapshot, double elapsedSeconds)
    {
        var previousPeers = _previousSnapshot?.Peers.ToDictionary(
            PeerKey,
            StringComparer.Ordinal) ?? new Dictionary<string, VpnPeerInfo>(StringComparer.Ordinal);

        var now = snapshot.Timestamp;
        var clients = snapshot.Peers
            .Select(peer => new VpnClientViewModel(
                peer,
                previousPeers.GetValueOrDefault(PeerKey(peer)),
                elapsedSeconds,
                now))
            .OrderByDescending(x => x.IsOnline)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Clients.Clear();
        foreach (var client in clients)
            Clients.Add(client);

        ClientsTotal = Clients.Count.ToString();
        ClientsOnline = Clients.Count(x => x.IsOnline).ToString();

        var downloaded = snapshot.Peers.Sum(x => x.SentBytes);
        var uploaded = snapshot.Peers.Sum(x => x.ReceivedBytes);

        TrafficReceived = snapshot.Peers.Count > 0 ? FormatBytes(downloaded) : "—";
        TrafficSent = snapshot.Peers.Count > 0 ? FormatBytes(uploaded) : "—";
    }


    private async Task SaveHistorySampleAsync(
        ServerConnection connection,
        ServerMonitorSnapshot snapshot,
        ServerMonitorSnapshot? previousSnapshot,
        CancellationToken cancellationToken)
    {
        if (_historyStore is null || !snapshot.Success)
            return;

        if (_lastHistorySave.HasValue &&
            snapshot.Timestamp - _lastHistorySave.Value < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            var elapsedSeconds = previousSnapshot is null
                ? 0
                : Math.Max(0.001, (snapshot.Timestamp - previousSnapshot.Timestamp).TotalSeconds);

            var downloadRate = 0d;
            var uploadRate = 0d;

            if (previousSnapshot is not null &&
                snapshot.NetworkReceivedBytes >= previousSnapshot.NetworkReceivedBytes &&
                snapshot.NetworkSentBytes >= previousSnapshot.NetworkSentBytes)
            {
                downloadRate =
                    (snapshot.NetworkReceivedBytes - previousSnapshot.NetworkReceivedBytes) / elapsedSeconds;
                uploadRate =
                    (snapshot.NetworkSentBytes - previousSnapshot.NetworkSentBytes) / elapsedSeconds;
            }

            var nowUnix = snapshot.Timestamp.ToUnixTimeSeconds();
            var clientsOnline = snapshot.Peers.Count(peer =>
                peer.LatestHandshakeUnix > 0 &&
                nowUnix >= peer.LatestHandshakeUnix &&
                nowUnix - peer.LatestHandshakeUnix <= 180);

            var record = new MonitoringHistoryRecord
            {
                ServerKey = BuildServerKey(connection),
                ServerName = connection.Name,
                CapturedAt = snapshot.Timestamp,
                CpuPercent = ParsePercent(snapshot.CpuUsage),
                RamPercent = ParsePercent(snapshot.MemoryUsage),
                DiskPercent = ParsePercent(snapshot.DiskUsage),
                DownloadBytesPerSecond = Math.Max(0, downloadRate),
                UploadBytesPerSecond = Math.Max(0, uploadRate),
                ClientsOnline = clientsOnline,
                ClientsTotal = snapshot.Peers.Count,
                TrafficReceivedBytes = snapshot.Peers.Sum(x => x.SentBytes),
                TrafficSentBytes = snapshot.Peers.Sum(x => x.ReceivedBytes)
            };

            var clientTrafficRecords = new List<ClientTrafficHistoryRecord>(snapshot.Peers.Count);
            var currentCounters = new Dictionary<string, (long Downloaded, long Uploaded)>(StringComparer.Ordinal);

            foreach (var peer in snapshot.Peers)
            {
                var clientKey = BuildClientTrafficKey(peer);
                var downloaded = Math.Max(0, peer.SentBytes);
                var uploaded = Math.Max(0, peer.ReceivedBytes);

                var downloadDelta = 0L;
                var uploadDelta = 0L;

                if (_lastSavedClientCounters.TryGetValue(clientKey, out var previousCounters))
                {
                    downloadDelta = CalculateCounterDelta(downloaded, previousCounters.Downloaded);
                    uploadDelta = CalculateCounterDelta(uploaded, previousCounters.Uploaded);
                }

                currentCounters[clientKey] = (downloaded, uploaded);
                clientTrafficRecords.Add(new ClientTrafficHistoryRecord
                {
                    ServerKey = record.ServerKey,
                    CapturedAt = snapshot.Timestamp,
                    ClientKey = clientKey,
                    ClientId = peer.ClientId,
                    ClientName = peer.ClientName,
                    ContainerName = peer.ContainerName,
                    ProtocolName = peer.ProtocolName,
                    AllowedIps = peer.AllowedIps,
                    DownloadTotalBytes = downloaded,
                    UploadTotalBytes = uploaded,
                    DownloadDeltaBytes = downloadDelta,
                    UploadDeltaBytes = uploadDelta
                });
            }

            await _historyStore.AppendAsync(record, cancellationToken);
            await _historyStore.AppendClientTrafficAsync(clientTrafficRecords, cancellationToken);

            _lastSavedClientCounters.Clear();
            foreach (var item in currentCounters)
                _lastSavedClientCounters[item.Key] = item.Value;

            _lastHistorySave = snapshot.Timestamp;

            if (!_historyCleanupDone)
            {
                _historyCleanupDone = true;
                await _historyStore.CleanupOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-90),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ошибка локальной истории не должна останавливать SSH-мониторинг.
        }
    }


    private static string BuildClientTrafficKey(VpnPeerInfo peer)
    {
        var allowedIps = string.Join(",", (peer.AllowedIps ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        var stablePart = string.IsNullOrWhiteSpace(allowedIps)
            ? peer.ClientId.Trim()
            : allowedIps.ToLowerInvariant();

        return $"{peer.ContainerName.Trim().ToLowerInvariant()}|{stablePart}";
    }

    private static long CalculateCounterDelta(long current, long previous)
    {
        if (current < 0)
            return 0;

        // WireGuard/AWG counters reset when an interface/container restarts.
        // In that case the current value is the amount transferred since the reset.
        return current >= previous ? current - previous : current;
    }

    private static string BuildServerKey(ServerConnection connection)
    {
        return $"{connection.Username.Trim().ToLowerInvariant()}@{connection.Host.Trim().ToLowerInvariant()}:{connection.Port}";
    }

    private static double? ParsePercent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().TrimEnd('%').Replace(',', '.');
        if (double.TryParse(
                normalized,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return Math.Clamp(parsed, 0, 100);
        }

        return null;
    }

    private static string PeerKey(VpnPeerInfo peer)
    {
        return $"{peer.ContainerName}|{peer.ClientId}";
    }

    private static string ValueOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} {LocalizationService.T("B", "Б")}";

        var value = (double)bytes;
        string[] units = LocalizationService.IsRussian ? ["Б", "КБ", "МБ", "ГБ", "ТБ"] : ["B", "KB", "MB", "GB", "TB"];
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
        string[] units = LocalizationService.IsRussian ? ["Б/с", "КБ/с", "МБ/с", "ГБ/с"] : ["B/s", "KB/s", "MB/s", "GB/s"];
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
}
