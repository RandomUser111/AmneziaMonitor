using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private static readonly string[] CategoryKeys = ["All", "SSH", "Monitoring", "Servers", "Clients", "Protocols", "Docker"];

    private readonly DashboardViewModel _dashboard;
    private readonly AppEventLogService _eventLog;
    private readonly IDockerLogService _dockerLogService;
    private string? _preferredContainerName;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _categoryFilterIndex;

    [ObservableProperty]
    private ProtocolStatusViewModel? _selectedContainer;

    [ObservableProperty]
    private decimal _tailLines = 200;

    [ObservableProperty]
    private string _dockerLogs = LocalizationService.T(
        "Select a container and click Refresh.",
        "Выберите контейнер и нажмите «Обновить».");

    [ObservableProperty]
    private string _dockerStatus = string.Empty;

    [ObservableProperty]
    private bool _isDockerBusy;

    public LogsViewModel(
        DashboardViewModel dashboard,
        AppEventLogService eventLog,
        IDockerLogService dockerLogService)
    {
        _dashboard = dashboard;
        _eventLog = eventLog;
        _dockerLogService = dockerLogService;

        RefreshCategories();
        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
        _eventLog.Entries.CollectionChanged += EntriesOnCollectionChanged;
        _dashboard.Protocols.CollectionChanged += ProtocolsOnCollectionChanged;
        _dashboard.PropertyChanged += DashboardOnPropertyChanged;

        RebuildEvents();
        SelectDefaultContainer();
    }

    public ObservableCollection<AppLogEntry> FilteredEvents { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    public ObservableCollection<ProtocolStatusViewModel> Containers => _dashboard.Protocols;

    public bool HasEvents => FilteredEvents.Count > 0;

    public bool HasNoEvents => !HasEvents;

    public bool HasContainers => Containers.Count > 0;

    public bool HasNoContainers => !HasContainers;

    public bool CanReadDockerLogs =>
        _dashboard.IsConnected && SelectedContainer is not null && !IsDockerBusy;

    public string ServerText => _dashboard.CurrentConnection is null
        ? LocalizationService.T("Server not connected", "Сервер не подключён")
        : $"{_dashboard.CurrentConnection.Name} · {_dashboard.CurrentConnection.Host}";

    public void ClearEvents()
    {
        _eventLog.Clear();
        RebuildEvents();
    }

    public async Task RefreshDockerLogsAsync()
    {
        var connection = _dashboard.CurrentConnection;
        var container = SelectedContainer;

        if (connection is null)
        {
            DockerStatus = LocalizationService.T("Connect to a server first.", "Сначала подключитесь к серверу.");
            return;
        }

        if (container is null)
        {
            DockerStatus = LocalizationService.T("Select a Docker container.", "Выберите Docker-контейнер.");
            return;
        }

        IsDockerBusy = true;
        DockerStatus = LocalizationService.T(
            $"Reading the last {(int)TailLines} lines from {container.ContainerName}…",
            $"Чтение последних {(int)TailLines} строк {container.ContainerName}…");

        try
        {
            var result = await _dockerLogService.GetLogsAsync(
                connection,
                container.ContainerName,
                (int)TailLines);

            if (result.Success)
            {
                DockerLogs = string.IsNullOrWhiteSpace(result.Content)
                    ? LocalizationService.T("Docker logs are empty.", "Docker logs пуст.")
                    : result.Content;

                var driverSuffix = string.IsNullOrWhiteSpace(result.LoggingDriver)
                    ? string.Empty
                    : $" · driver: {result.LoggingDriver}";

                DockerStatus = LocalizationService.T(
                    $"Updated {DateTime.Now:HH:mm:ss} · {container.ContainerName}{driverSuffix}",
                    $"Обновлено {DateTime.Now:HH:mm:ss} · {container.ContainerName}{driverSuffix}");
                _eventLog.Info("Docker", LocalizationService.T(
                    $"Read logs for container {container.ContainerName}.",
                    $"Прочитан журнал контейнера {container.ContainerName}."));
            }
            else if (result.IsUnavailable)
            {
                DockerLogs = LocalizationService.TranslateExternalMessage(result.ErrorMessage);

                var driverSuffix = string.IsNullOrWhiteSpace(result.LoggingDriver)
                    ? string.Empty
                    : $" · driver: {result.LoggingDriver}";

                DockerStatus = LocalizationService.T(
                    $"Docker logs unavailable{driverSuffix}",
                    $"Docker logs недоступны{driverSuffix}");
                _eventLog.Warning("Docker", LocalizationService.T(
                    $"Logs for {container.ContainerName} are unavailable: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}",
                    $"Журнал {container.ContainerName} недоступен: {result.ErrorMessage}"));
            }
            else
            {
                DockerStatus = LocalizationService.T(
                    $"Error: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}",
                    $"Ошибка: {result.ErrorMessage}");
                _eventLog.Error("Docker", LocalizationService.T(
                    $"Could not read {container.ContainerName}: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}",
                    $"Не удалось прочитать {container.ContainerName}: {result.ErrorMessage}"));
            }
        }
        finally
        {
            IsDockerBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RebuildEvents();

    partial void OnCategoryFilterIndexChanged(int value) => RebuildEvents();

    partial void OnSelectedContainerChanged(ProtocolStatusViewModel? value)
    {
        if (value is not null)
            _preferredContainerName = value.ContainerName;

        OnPropertyChanged(nameof(CanReadDockerLogs));
        DockerStatus = string.Empty;
    }

    partial void OnIsDockerBusyChanged(bool value) => OnPropertyChanged(nameof(CanReadDockerLogs));

    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshCategories();
        OnPropertyChanged(nameof(ServerText));
        RebuildEvents();
    }

    private void RefreshCategories()
    {
        var selected = Math.Clamp(CategoryFilterIndex, 0, CategoryKeys.Length - 1);
        Categories.Clear();
        Categories.Add(LocalizationService.T("All", "Все"));
        Categories.Add("SSH");
        Categories.Add(LocalizationService.T("Monitoring", "Мониторинг"));
        Categories.Add(LocalizationService.T("Servers", "Серверы"));
        Categories.Add(LocalizationService.T("Clients", "Клиенты"));
        Categories.Add(LocalizationService.T("Protocols", "Протоколы"));
        Categories.Add("Docker");
        CategoryFilterIndex = selected;
    }

    private void EntriesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildEvents();

    private void ProtocolsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SelectDefaultContainer();
        OnPropertyChanged(nameof(HasContainers));
        OnPropertyChanged(nameof(HasNoContainers));
        OnPropertyChanged(nameof(CanReadDockerLogs));
    }

    private void DashboardOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsConnected))
        {
            OnPropertyChanged(nameof(CanReadDockerLogs));
            OnPropertyChanged(nameof(ServerText));
        }
    }

    private void SelectDefaultContainer()
    {
        if (Containers.Count == 0)
            return;

        if (SelectedContainer is not null && Containers.Contains(SelectedContainer))
            return;

        var preferred = string.IsNullOrWhiteSpace(_preferredContainerName)
            ? null
            : Containers.FirstOrDefault(x => x.ContainerName == _preferredContainerName);

        SelectedContainer = preferred ?? Containers.FirstOrDefault(x => x.IsRunning) ?? Containers.FirstOrDefault();
    }

    private void RebuildEvents()
    {
        var query = SearchText.Trim();
        var categoryKey = CategoryFilterIndex >= 0 && CategoryFilterIndex < CategoryKeys.Length
            ? CategoryKeys[CategoryFilterIndex]
            : "All";

        var events = _eventLog.Entries.Where(entry =>
        {
            if (categoryKey != "All" && !AppEventLogService.NormalizeCategory(entry.Category).Equals(categoryKey, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            return entry.Message.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   entry.CategoryText.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   entry.LevelText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        });

        FilteredEvents.Clear();
        foreach (var entry in events)
            FilteredEvents.Add(entry);

        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasNoEvents));
    }
}
