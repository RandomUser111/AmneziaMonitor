using System.Collections.ObjectModel;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.App.ViewModels;

public sealed class ClientTrafficViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboard;
    private readonly IMonitoringHistoryStore _historyStore;
    private readonly List<ClientTrafficRowViewModel> _allRows = [];
    private int _periodIndex = 2;
    private bool _isLoading;
    private string _searchText = string.Empty;
    private string _serverTitle = LocalizationService.T("No server selected", "Сервер не выбран");
    private string _statusText = LocalizationService.T("Connect to a server to view client traffic.", "Подключитесь к серверу, чтобы увидеть трафик клиентов.");
    private string _periodText = LocalizationService.T("Last 24 hours", "Последние 24 часа");
    private string _downloadSummary = "—";
    private string _uploadSummary = "—";
    private string _totalSummary = "—";
    private string _clientsSummary = "—";

    public ClientTrafficViewModel(
        DashboardViewModel dashboard,
        IMonitoringHistoryStore historyStore)
    {
        _dashboard = dashboard;
        _historyStore = historyStore;
        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
    }

    public ObservableCollection<ClientTrafficRowViewModel> Rows { get; } = [];

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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value ?? string.Empty;
            OnPropertyChanged(nameof(SearchText));
            RebuildFilter();
        }
    }

    public string ServerTitle
    {
        get => _serverTitle;
        private set { _serverTitle = value; OnPropertyChanged(nameof(ServerTitle)); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    public string PeriodText
    {
        get => _periodText;
        private set { _periodText = value; OnPropertyChanged(nameof(PeriodText)); }
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

    public string TotalSummary
    {
        get => _totalSummary;
        private set { _totalSummary = value; OnPropertyChanged(nameof(TotalSummary)); }
    }

    public string ClientsSummary
    {
        get => _clientsSummary;
        private set { _clientsSummary = value; OnPropertyChanged(nameof(ClientsSummary)); }
    }

    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => !HasRows;

    public async Task SetPeriodAsync(int periodIndex)
    {
        PeriodIndex = Math.Clamp(periodIndex, 0, 4);
        await RefreshAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        var connection = _dashboard.CurrentConnection;
        if (connection is null)
        {
            Reset(LocalizationService.T(
                "Connect to a server to view client traffic.",
                "Подключитесь к серверу, чтобы увидеть трафик клиентов."));
            return;
        }

        IsLoading = true;
        try
        {
            var duration = GetDuration();
            PeriodText = GetPeriodText();
            ServerTitle = $"{connection.Name} · {connection.Host}";
            StatusText = LocalizationService.T("Loading traffic statistics…", "Загрузка статистики трафика…");

            var now = DateTimeOffset.UtcNow;
            var summaries = await _historyStore.GetClientTrafficSummaryAsync(
                BuildServerKey(connection),
                now - duration,
                now,
                cancellationToken);

            ApplySummaries(summaries);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.T("Loading cancelled.", "Загрузка отменена.");
        }
        catch (Exception ex)
        {
            Reset(LocalizationService.T(
                $"Could not read client traffic statistics: {ex.Message}",
                $"Не удалось прочитать статистику трафика клиентов: {ex.Message}"),
                preserveServer: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySummaries(IReadOnlyList<ClientTrafficSummary> summaries)
    {
        _allRows.Clear();

        var allBytes = summaries.Sum(x => Math.Max(0, x.TotalBytes));
        foreach (var summary in summaries.OrderByDescending(x => x.TotalBytes))
            _allRows.Add(new ClientTrafficRowViewModel(summary, allBytes));

        var download = summaries.Sum(x => Math.Max(0, x.DownloadBytes));
        var upload = summaries.Sum(x => Math.Max(0, x.UploadBytes));

        DownloadSummary = FormatBytes(download);
        UploadSummary = FormatBytes(upload);
        TotalSummary = FormatBytes(download + upload);
        ClientsSummary = summaries.Count.ToString();
        StatusText = summaries.Count == 0
            ? LocalizationService.T(
                "No per-client traffic data has been collected for the selected period yet.",
                "За выбранный период статистика трафика клиентов пока не накоплена.")
            : LocalizationService.T($"Updated {DateTime.Now:HH:mm:ss}", $"Обновлено {DateTime.Now:HH:mm:ss}");

        RebuildFilter();
    }

    private void RebuildFilter()
    {
        var query = SearchText.Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? _allRows
            : _allRows.Where(x =>
                Contains(x.Name, query) ||
                Contains(x.Protocol, query) ||
                Contains(x.Address, query) ||
                Contains(x.ClientId, query)).ToList();

        Rows.Clear();
        foreach (var row in source)
            Rows.Add(row);

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
    }

    private void Reset(string status, bool preserveServer = false)
    {
        _allRows.Clear();
        Rows.Clear();
        if (!preserveServer)
            ServerTitle = LocalizationService.T("No server selected", "Сервер не выбран");
        StatusText = status;
        DownloadSummary = "—";
        UploadSummary = "—";
        TotalSummary = "—";
        ClientsSummary = "—";
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
    }

    private TimeSpan GetDuration() => PeriodIndex switch
    {
        0 => TimeSpan.FromHours(1),
        1 => TimeSpan.FromHours(6),
        2 => TimeSpan.FromHours(24),
        3 => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(30)
    };

    private string GetPeriodText() => PeriodIndex switch
    {
        0 => LocalizationService.T("Last hour", "Последний час"),
        1 => LocalizationService.T("Last 6 hours", "Последние 6 часов"),
        2 => LocalizationService.T("Last 24 hours", "Последние 24 часа"),
        3 => LocalizationService.T("Last 7 days", "Последние 7 дней"),
        _ => LocalizationService.T("Last 30 days", "Последние 30 дней")
    };

    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        PeriodText = GetPeriodText();
        foreach (var row in _allRows)
            row.NotifyLocalizationChanged();
        _ = RefreshAsync();
    }

    private static string BuildServerKey(ServerConnection connection) =>
        $"{connection.Username.Trim().ToLowerInvariant()}@{connection.Host.Trim().ToLowerInvariant()}:{connection.Port}";

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);

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
