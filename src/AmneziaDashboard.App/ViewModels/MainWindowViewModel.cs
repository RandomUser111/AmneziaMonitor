using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.Infrastructure.Security;
using AmneziaDashboard.Infrastructure.Ssh;
using AmneziaDashboard.Infrastructure.Storage;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmneziaDashboard.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly ServersViewModel _serversViewModel;
    private readonly ClientsViewModel _clientsViewModel;
    private readonly ProtocolsViewModel _protocolsViewModel;
    private readonly HistoryViewModel _historyViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ISecretStore _secretStore;
    private bool _autoConnectAttempted;
    private bool _synchronizingQuickSelection;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private ServerProfileItemViewModel? _selectedQuickServer;

    [ObservableProperty]
    private string _quickSwitchStatus = string.Empty;

    public DashboardViewModel Dashboard => _dashboardViewModel;

    public ServersViewModel ServersPage => _serversViewModel;

    public ObservableCollection<ServerProfileItemViewModel> Servers => _serversViewModel.Servers;

    public bool HasSavedServers => Servers.Count > 0;

    public string? CurrentServerProfileId => _serversViewModel.CurrentServerProfileId;

    public bool IsSynchronizingQuickSelection => _synchronizingQuickSelection;

    public MainWindowViewModel()
        : this(
            new JsonServerProfileStore(),
            new CrossPlatformSecretStore(),
            new SshServerProbeService())
    {
    }

    public MainWindowViewModel(
        IServerProfileStore profileStore,
        ISecretStore secretStore,
        IServerProbeService probeService)
    {
        _secretStore = secretStore;

        var eventLog = new AppEventLogService();
        var historyStore = new SqliteMonitoringHistoryStore();
        _dashboardViewModel = new DashboardViewModel(
            new SshServerMonitorService(),
            historyStore,
            eventLog);
        _serversViewModel = new ServersViewModel(
            _dashboardViewModel,
            profileStore,
            secretStore,
            probeService,
            eventLog);

        _serversViewModel.Servers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedServers));
            SynchronizeQuickSelection();
        };

        _serversViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServersViewModel.CurrentServerProfileId))
            {
                QuickSwitchStatus = string.Empty;
                SynchronizeQuickSelection();
            }
        };

        _clientsViewModel = new ClientsViewModel(_dashboardViewModel, new SshClientManagementService(), eventLog);
        _protocolsViewModel = new ProtocolsViewModel(_dashboardViewModel, new SshProtocolManagementService(), eventLog);
        _historyViewModel = new HistoryViewModel(_dashboardViewModel, historyStore);
        _logsViewModel = new LogsViewModel(_dashboardViewModel, eventLog, new SshDockerLogService());
        _settingsViewModel = new SettingsViewModel();
        _currentPage = _dashboardViewModel;
    }

    public async Task TryAutoConnectAsync()
    {
        if (_autoConnectAttempted)
            return;

        _autoConnectAttempted = true;

        try
        {
            await RefreshServersAsync();

            if (!_secretStore.IsAvailable)
                return;

            var profileItem = Servers
                .Where(x => x.Profile.AutoConnect && x.Profile.RememberPassword)
                .OrderByDescending(x => x.Profile.LastUsedAt)
                .FirstOrDefault();

            if (profileItem is null)
                return;

            var result = await _serversViewModel.ConnectAsync(profileItem);
            QuickSwitchStatus = result.Success ? string.Empty : result.ErrorMessage;
            SynchronizeQuickSelection();
        }
        catch
        {
            // Автоподключение не должно мешать ручному запуску приложения.
        }
    }

    public async Task RefreshServersAsync()
    {
        await _serversViewModel.RefreshAsync();
        OnPropertyChanged(nameof(HasSavedServers));
        SynchronizeQuickSelection();
    }

    public async Task<OperationResult> ConnectToServerAsync(ServerProfileItemViewModel item)
    {
        var result = await _serversViewModel.ConnectAsync(item);
        QuickSwitchStatus = result.Success ? string.Empty : result.ErrorMessage;
        OnPropertyChanged(nameof(HasSavedServers));
        SynchronizeQuickSelection();
        return result;
    }

    public async Task AcceptConnectionAsync(ServerConnection connection, ServerProbeResult probe)
    {
        await _serversViewModel.AcceptConnectionAsync(connection, probe);
        QuickSwitchStatus = string.Empty;
        OnPropertyChanged(nameof(HasSavedServers));
        SynchronizeQuickSelection();
    }

    public void ShowServersPage()
    {
        CurrentPage = _serversViewModel;
    }

    private void SynchronizeQuickSelection()
    {
        try
        {
            _synchronizingQuickSelection = true;
            SelectedQuickServer = _serversViewModel.FindCurrent();
        }
        finally
        {
            _synchronizingQuickSelection = false;
        }
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        CurrentPage = _dashboardViewModel;
    }

    [RelayCommand]
    private void ShowServers()
    {
        ShowServersPage();
    }

    [RelayCommand]
    private void ShowClients()
    {
        _clientsViewModel.NotifyClientsChanged();
        CurrentPage = _clientsViewModel;
    }

    [RelayCommand]
    private void ShowProtocols()
    {
        CurrentPage = _protocolsViewModel;
    }

    [RelayCommand]
    private async Task ShowHistory()
    {
        CurrentPage = _historyViewModel;
        await _historyViewModel.RefreshAsync();
    }

    [RelayCommand]
    private void ShowLogs()
    {
        CurrentPage = _logsViewModel;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = _settingsViewModel;
    }
}
