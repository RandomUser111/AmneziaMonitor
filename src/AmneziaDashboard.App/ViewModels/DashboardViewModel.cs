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
    private CancellationTokenSource? _monitorCancellation;
    private ServerMonitorSnapshot? _previousSnapshot;
    private DateTimeOffset? _lastHistorySave;
    private bool _historyCleanupDone;

    [ObservableProperty]
    private string _serverStatus = "Не подключён";

    [ObservableProperty]
    private IBrush _serverStatusBrush = OfflineBrush;

    [ObservableProperty]
    private string _serverAddress = "Сервер не выбран";

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
    private string _protocolsMessage = "Подключитесь к серверу для поиска контейнеров Amnezia.";

    [ObservableProperty]
    private string _connectButtonText = "Подключить сервер";

    [ObservableProperty]
    private string _dockerVersion = "—";

    [ObservableProperty]
    private string _operatingSystem = "—";

    [ObservableProperty]
    private string _monitorStatus = "Мониторинг остановлен";

    public ObservableCollection<ProtocolStatusViewModel> Protocols { get; } = [];

    public ObservableCollection<VpnClientViewModel> Clients { get; } = [];

    public ServerConnection? CurrentConnection { get; private set; }

    public DashboardViewModel(
        IServerMonitorService monitorService,
        IMonitoringHistoryStore? historyStore = null,
        AppEventLogService? eventLog = null)
    {
        _monitorService = monitorService;
        _historyStore = historyStore;
        _eventLog = eventLog;
    }

    public void ApplyConnection(
        ServerConnection connection,
        ServerProbeResult probe)
    {
        StopMonitoring();

        CurrentConnection = connection;
        IsConnected = true;
        ServerStatus = "Подключён";
        ServerStatusBrush = OnlineBrush;
        ConnectButtonText = "Сменить сервер";
        MonitorStatus = "Запуск мониторинга…";

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

        _eventLog?.Success("SSH", $"Подключено к {connection.Name} ({connection.Host}:{connection.Port}).");
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
            _eventLog?.Success("Мониторинг", "Связь с сервером восстановлена.");
        ServerStatus = "Подключён";
        ServerStatusBrush = OnlineBrush;
        MonitorStatus = $"Обновлено {DateTime.Now:HH:mm:ss}";

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

        ApplyProtocols(snapshot.AmneziaContainers);
        ApplyClients(snapshot, elapsedSeconds);

        _previousSnapshot = snapshot;
    }

    private void ApplyMonitorError(string message)
    {
        var wasConnected = IsConnected;
        IsConnected = false;
        if (wasConnected)
            _eventLog?.Warning("Мониторинг", string.IsNullOrWhiteSpace(message)
                ? "Потеряна связь с сервером."
                : $"Потеряна связь с сервером: {message}");
        ServerStatus = "Нет связи";
        ServerStatusBrush = ErrorBrush;
        MonitorStatus = string.IsNullOrWhiteSpace(message)
            ? "Ошибка обновления"
            : $"Ошибка: {message}";
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
            : "Контейнеры Amnezia не найдены.";
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

            await _historyStore.AppendAsync(record, cancellationToken);
            _lastHistorySave = snapshot.Timestamp;

            if (!_historyCleanupDone)
            {
                _historyCleanupDone = true;
                await _historyStore.CleanupOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-30),
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
}
