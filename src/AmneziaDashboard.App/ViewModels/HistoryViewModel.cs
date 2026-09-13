using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;

namespace AmneziaDashboard.App.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboard;
    private readonly IMonitoringHistoryStore _historyStore;
    private int _periodIndex = 2;
    private bool _isLoading;
    private string _statusText = LocalizationService.T("Connect to a server to view history.", "Подключитесь к серверу, чтобы увидеть историю.");
    private string _serverTitle = LocalizationService.T("No server selected", "Сервер не выбран");
    private string _periodText = LocalizationService.T("Last 24 hours", "Последние 24 часа");
    private string _cpuSummary = "—";
    private string _ramSummary = "—";
    private string _downloadSummary = "—";
    private string _uploadSummary = "—";
    private string _clientsSummary = "—";
    private string _sampleSummary = "—";
    private bool _hasData;
    private IReadOnlyList<double> _cpuValues = [];
    private IReadOnlyList<double> _ramValues = [];
    private IReadOnlyList<double> _downloadValues = [];
    private IReadOnlyList<double> _uploadValues = [];
    private IReadOnlyList<double> _clientsValues = [];

    public HistoryViewModel(
        DashboardViewModel dashboard,
        IMonitoringHistoryStore historyStore)
    {
        _dashboard = dashboard;
        _historyStore = historyStore;
    }

    public int PeriodIndex
    {
        get => _periodIndex;
        private set
        {
            if (_periodIndex == value)
                return;

            _periodIndex = value;
            OnPropertyChanged(nameof(PeriodIndex));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
                return;

            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;

            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string ServerTitle
    {
        get => _serverTitle;
        private set
        {
            if (_serverTitle == value)
                return;

            _serverTitle = value;
            OnPropertyChanged(nameof(ServerTitle));
        }
    }

    public string PeriodText
    {
        get => _periodText;
        private set
        {
            if (_periodText == value)
                return;

            _periodText = value;
            OnPropertyChanged(nameof(PeriodText));
        }
    }

    public string CpuSummary
    {
        get => _cpuSummary;
        private set { _cpuSummary = value; OnPropertyChanged(nameof(CpuSummary)); }
    }

    public string RamSummary
    {
        get => _ramSummary;
        private set { _ramSummary = value; OnPropertyChanged(nameof(RamSummary)); }
    }

    public string DownloadSummary
    {
        get => _downloadSummary;
        private set { _downloadSummary = value; OnPropertyChanged(nameof(DownloadSummary)); }
    }

    public string UploadSummary
    {
        get => _uploadSummary;
        private set { _uploadSummary = value; OnPropertyChanged(nameof(UploadSummary)); }
    }

    public string ClientsSummary
    {
        get => _clientsSummary;
        private set { _clientsSummary = value; OnPropertyChanged(nameof(ClientsSummary)); }
    }

    public string SampleSummary
    {
        get => _sampleSummary;
        private set { _sampleSummary = value; OnPropertyChanged(nameof(SampleSummary)); }
    }

    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (_hasData == value)
                return;

            _hasData = value;
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasNoData));
        }
    }

    public bool HasNoData => !HasData;

    public IReadOnlyList<double> CpuValues
    {
        get => _cpuValues;
        private set { _cpuValues = value; OnPropertyChanged(nameof(CpuValues)); }
    }

    public IReadOnlyList<double> RamValues
    {
        get => _ramValues;
        private set { _ramValues = value; OnPropertyChanged(nameof(RamValues)); }
    }

    public IReadOnlyList<double> DownloadValues
    {
        get => _downloadValues;
        private set { _downloadValues = value; OnPropertyChanged(nameof(DownloadValues)); }
    }

    public IReadOnlyList<double> UploadValues
    {
        get => _uploadValues;
        private set { _uploadValues = value; OnPropertyChanged(nameof(UploadValues)); }
    }

    public IReadOnlyList<double> ClientsValues
    {
        get => _clientsValues;
        private set { _clientsValues = value; OnPropertyChanged(nameof(ClientsValues)); }
    }

    public async Task SetPeriodAsync(int periodIndex)
    {
        PeriodIndex = Math.Clamp(periodIndex, 0, 3);
        await RefreshAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        var connection = _dashboard.CurrentConnection;
        if (connection is null)
        {
            Reset(LocalizationService.T("Connect to a server to view history.", "Подключитесь к серверу, чтобы увидеть историю."));
            return;
        }

        IsLoading = true;
        try
        {
            var duration = PeriodIndex switch
            {
                0 => TimeSpan.FromHours(1),
                1 => TimeSpan.FromHours(6),
                2 => TimeSpan.FromHours(24),
                _ => TimeSpan.FromDays(7)
            };

            PeriodText = PeriodIndex switch
            {
                0 => LocalizationService.T("Last hour", "Последний час"),
                1 => LocalizationService.T("Last 6 hours", "Последние 6 часов"),
                2 => LocalizationService.T("Last 24 hours", "Последние 24 часа"),
                _ => LocalizationService.T("Last 7 days", "Последние 7 дней")
            };

            ServerTitle = $"{connection.Name} · {connection.Host}";
            StatusText = LocalizationService.T("Loading history…", "Загрузка истории…");

            var now = DateTimeOffset.UtcNow;
            var records = await _historyStore.GetRangeAsync(
                BuildServerKey(connection),
                now - duration,
                now,
                cancellationToken);

            if (records.Count == 0)
            {
                Reset(LocalizationService.T("No history has been collected for the selected period yet.", "История за выбранный период пока не накоплена."), preserveServer: true);
                return;
            }

            ApplyRecords(records);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.T("Loading cancelled.", "Загрузка отменена.");
        }
        catch (Exception ex)
        {
            Reset(LocalizationService.T($"Could not read history: {ex.Message}", $"Не удалось прочитать историю: {ex.Message}"), preserveServer: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyRecords(IReadOnlyList<MonitoringHistoryRecord> records)
    {
        HasData = true;
        StatusText = LocalizationService.T($"Updated {DateTime.Now:HH:mm:ss}", $"Обновлено {DateTime.Now:HH:mm:ss}");

        CpuValues = Downsample(records.Where(x => x.CpuPercent.HasValue).Select(x => x.CpuPercent!.Value));
        RamValues = Downsample(records.Where(x => x.RamPercent.HasValue).Select(x => x.RamPercent!.Value));
        DownloadValues = Downsample(records.Select(x => x.DownloadBytesPerSecond));
        UploadValues = Downsample(records.Select(x => x.UploadBytesPerSecond));
        ClientsValues = Downsample(records.Select(x => (double)x.ClientsOnline));

        CpuSummary = FormatPercentSummary(records.Select(x => x.CpuPercent));
        RamSummary = FormatPercentSummary(records.Select(x => x.RamPercent));
        DownloadSummary = FormatRateSummary(records.Select(x => x.DownloadBytesPerSecond));
        UploadSummary = FormatRateSummary(records.Select(x => x.UploadBytesPerSecond));

        var onlineAverage = records.Average(x => x.ClientsOnline);
        var onlineMax = records.Max(x => x.ClientsOnline);
        var totalMax = records.Max(x => x.ClientsTotal);
        ClientsSummary = LocalizationService.IsRussian
            ? $"ср. {onlineAverage:0.#} · макс. {onlineMax} · всего до {totalMax}"
            : $"avg. {onlineAverage:0.#} · max. {onlineMax} · total up to {totalMax}";

        var first = records[0].CapturedAt.ToLocalTime();
        var last = records[^1].CapturedAt.ToLocalTime();
        SampleSummary = LocalizationService.IsRussian
            ? $"{records.Count} точек · {first:dd.MM HH:mm} — {last:dd.MM HH:mm}"
            : $"{records.Count} samples · {first:g} — {last:g}";
    }

    private void Reset(string status, bool preserveServer = false)
    {
        HasData = false;
        StatusText = status;
        if (!preserveServer)
            ServerTitle = LocalizationService.T("No server selected", "Сервер не выбран");

        CpuValues = [];
        RamValues = [];
        DownloadValues = [];
        UploadValues = [];
        ClientsValues = [];
        CpuSummary = "—";
        RamSummary = "—";
        DownloadSummary = "—";
        UploadSummary = "—";
        ClientsSummary = "—";
        SampleSummary = "—";
    }

    private static IReadOnlyList<double> Downsample(IEnumerable<double> source)
    {
        var values = source.ToList();
        if (values.Count <= 240)
            return values;

        var bucketSize = (int)Math.Ceiling(values.Count / 240d);
        var result = new List<double>();

        for (var i = 0; i < values.Count; i += bucketSize)
        {
            var count = Math.Min(bucketSize, values.Count - i);
            result.Add(values.Skip(i).Take(count).Average());
        }

        return result;
    }

    private static string FormatPercentSummary(IEnumerable<double?> source)
    {
        var values = source.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (values.Count == 0)
            return "—";

        return LocalizationService.IsRussian
            ? $"ср. {values.Average():0.#}% · макс. {values.Max():0.#}%"
            : $"avg. {values.Average():0.#}% · max. {values.Max():0.#}%";
    }

    private static string FormatRateSummary(IEnumerable<double> source)
    {
        var values = source.ToList();
        if (values.Count == 0)
            return "—";

        return LocalizationService.IsRussian
            ? $"ср. {FormatRate(values.Average())} · макс. {FormatRate(values.Max())}"
            : $"avg. {FormatRate(values.Average())} · max. {FormatRate(values.Max())}";
    }

    private static string FormatRate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        string[] units = LocalizationService.IsRussian ? ["Б/с", "КБ/с", "МБ/с", "ГБ/с"] : ["B/s", "KB/s", "MB/s", "GB/s"];
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    public static string BuildServerKey(ServerConnection connection)
    {
        return $"{connection.Username.Trim().ToLowerInvariant()}@{connection.Host.Trim().ToLowerInvariant()}:{connection.Port}";
    }
}
