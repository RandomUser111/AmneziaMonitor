using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class ClientsViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboard;
    private readonly IClientManagementService _clientManagementService;
    private readonly AppEventLogService? _eventLog;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _statusFilterIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _operationMessage = string.Empty;

    public ClientsViewModel(
        DashboardViewModel dashboard,
        IClientManagementService clientManagementService,
        AppEventLogService? eventLog = null)
    {
        _dashboard = dashboard;
        _clientManagementService = clientManagementService;
        _eventLog = eventLog;
        _dashboard.PropertyChanged += DashboardOnPropertyChanged;
        _dashboard.Clients.CollectionChanged += (_, _) => NotifyClientsChanged();
        _dashboard.Protocols.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCreateClient));
        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
        RebuildFilter();
    }

    public ObservableCollection<VpnClientViewModel> FilteredClients { get; } = [];

    public string ServerStatus => _dashboard.ServerStatus;

    public string MonitorStatus => _dashboard.MonitorStatus;

    public string ClientsOnline => _dashboard.ClientsOnline;

    public string ClientsTotal => _dashboard.ClientsTotal;

    public bool HasClients => _dashboard.Clients.Count > 0;

    public bool HasVisibleClients => FilteredClients.Count > 0;

    public bool HasNoClients => !HasClients;

    public bool HasNoVisibleClients => HasClients && !HasVisibleClients;

    public string EmptyMessage =>
        _dashboard.IsConnected
            ? LocalizationService.T("No WireGuard / AmneziaWG clients found.", "Для WireGuard / AmneziaWG клиенты не найдены.")
            : LocalizationService.T("Connect to a server first.", "Сначала подключитесь к серверу." );

    public bool CanCreateClient =>
        _dashboard.IsConnected && !IsBusy && GetCreatableProtocols().Count > 0;

    public IReadOnlyList<ProtocolStatusViewModel> GetCreatableProtocols() =>
        _dashboard.Protocols
            .Where(protocol =>
                protocol.IsRunning &&
                protocol.ProtocolKey is "awg2" or "awg" or "wireguard")
            .ToList();

    public async Task<CreateClientResult> CreateClientAsync(
        string containerName,
        string clientName)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return CreateClientResult.Fail(LocalizationService.T("Server is not connected.", "Сервер не подключён."));

        IsBusy = true;
        OperationMessage = LocalizationService.T($"Creating client {clientName}…", $"Создание клиента {clientName}…");

        try
        {
            var result = await _clientManagementService.CreateClientAsync(
                connection,
                containerName,
                clientName);

            OperationMessage = result.Success
                ? LocalizationService.T("Client created. Save the issued .conf profile.", "Клиент создан. Сохраните выданный .conf-профиль.")
                : LocalizationService.T($"Error: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка: {result.ErrorMessage}");
            if (result.Success)
                _eventLog?.Success("Clients", LocalizationService.T($"Client \"{clientName}\" created in {containerName}.", $"Создан клиент «{clientName}» в {containerName}."));
            else
                _eventLog?.Error("Clients", LocalizationService.T($"Failed to create \"{clientName}\": {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка создания «{clientName}»: {result.ErrorMessage}"));

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OperationResult> RenameClientAsync(
        VpnClientViewModel client,
        string newName)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return OperationResult.Fail(LocalizationService.T("Server is not connected.", "Сервер не подключён."));

        IsBusy = true;
        OperationMessage = LocalizationService.T($"Renaming {client.Name}…", $"Переименование {client.Name}…");

        try
        {
            var result = await _clientManagementService.RenameClientAsync(
                connection,
                client.ContainerName,
                client.ClientId,
                newName);

            OperationMessage = result.Success
                ? LocalizationService.T("Name saved. Data will refresh on the next monitoring cycle.", "Имя сохранено. Данные обновятся при следующем цикле мониторинга.")
                : LocalizationService.T($"Error: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка: {result.ErrorMessage}");
            if (result.Success)
                _eventLog?.Success("Clients", LocalizationService.T($"Client \"{client.Name}\" renamed to \"{newName}\".", $"Клиент «{client.Name}» переименован в «{newName}»."));
            else
                _eventLog?.Error("Clients", LocalizationService.T($"Failed to rename \"{client.Name}\": {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка переименования «{client.Name}»: {result.ErrorMessage}"));

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CreateClientResult> RestoreClientConfigAsync(VpnClientViewModel client)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return CreateClientResult.Fail(LocalizationService.T("Server is not connected.", "Сервер не подключён."));

        IsBusy = true;
        OperationMessage = LocalizationService.T($"Restoring configuration for {client.Name}…", $"Восстановление конфигурации {client.Name}…");

        try
        {
            var result = await _clientManagementService.RestoreClientConfigAsync(
                connection,
                client.ContainerName,
                client.ClientId,
                client.Name);

            OperationMessage = result.Success
                ? LocalizationService.T("Configuration restored. Save the new .conf profile.", "Конфигурация восстановлена. Сохраните новый .conf-профиль.")
                : LocalizationService.T($"Error: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка: {result.ErrorMessage}");
            if (result.Success)
                _eventLog?.Warning("Clients", LocalizationService.T($"Configuration and key pair reissued for client \"{client.Name}\".", $"Для клиента «{client.Name}» перевыпущена конфигурация и ключевая пара."));
            else
                _eventLog?.Error("Clients", LocalizationService.T($"Failed to restore \"{client.Name}\": {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка восстановления «{client.Name}»: {result.ErrorMessage}"));

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OperationResult> RevokeClientAsync(VpnClientViewModel client)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return OperationResult.Fail(LocalizationService.T("Server is not connected.", "Сервер не подключён."));

        IsBusy = true;
        OperationMessage = LocalizationService.T($"Revoking access for {client.Name}…", $"Отзыв доступа {client.Name}…");

        try
        {
            var result = await _clientManagementService.RevokeClientAsync(
                connection,
                client.ContainerName,
                client.ClientId);

            OperationMessage = result.Success
                ? (string.IsNullOrWhiteSpace(result.Message)
                    ? LocalizationService.T("Access revoked. The list will refresh on the next monitoring cycle.", "Доступ отозван. Список обновится при следующем цикле мониторинга.")
                    : LocalizationService.TranslateExternalMessage(result.Message))
                : LocalizationService.T($"Error: {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка: {result.ErrorMessage}");
            if (result.Success)
                _eventLog?.Warning("Clients", LocalizationService.T($"Access for client \"{client.Name}\" revoked.", $"Доступ клиента «{client.Name}» отозван."));
            else
                _eventLog?.Error("Clients", LocalizationService.T($"Failed to revoke \"{client.Name}\": {LocalizationService.TranslateExternalMessage(result.ErrorMessage)}", $"Ошибка отзыва «{client.Name}»: {result.ErrorMessage}"));

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }


    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var client in _dashboard.Clients)
            client.NotifyLocalizationChanged();

        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(ServerStatus));
        OnPropertyChanged(nameof(MonitorStatus));
        NotifyClientsChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildFilter();
    }

    partial void OnStatusFilterIndexChanged(int value)
    {
        RebuildFilter();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateClient));
    }

    private void DashboardOnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.ServerStatus))
            OnPropertyChanged(nameof(ServerStatus));

        if (e.PropertyName is nameof(DashboardViewModel.MonitorStatus))
            OnPropertyChanged(nameof(MonitorStatus));

        if (e.PropertyName is nameof(DashboardViewModel.ClientsOnline))
            OnPropertyChanged(nameof(ClientsOnline));

        if (e.PropertyName is nameof(DashboardViewModel.ClientsTotal))
            OnPropertyChanged(nameof(ClientsTotal));

        if (e.PropertyName is nameof(DashboardViewModel.IsConnected))
        {
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(CanCreateClient));
            NotifyVisibilityChanged();
        }
    }

    public void NotifyClientsChanged()
    {
        RebuildFilter();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void RebuildFilter()
    {
        var query = SearchText.Trim();

        var items = _dashboard.Clients.Where(client =>
        {
            var statusMatches = StatusFilterIndex switch
            {
                1 => client.IsOnline,
                2 => !client.IsOnline,
                _ => true
            };

            if (!statusMatches)
                return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            return Contains(client.Name, query) ||
                   Contains(client.Protocol, query) ||
                   Contains(client.Address, query) ||
                   Contains(client.Endpoint, query) ||
                   Contains(client.ClientId, query);
        });

        FilteredClients.Clear();
        foreach (var item in items)
            FilteredClients.Add(item);

        NotifyVisibilityChanged();
    }

    private void NotifyVisibilityChanged()
    {
        OnPropertyChanged(nameof(HasClients));
        OnPropertyChanged(nameof(HasNoClients));
        OnPropertyChanged(nameof(HasVisibleClients));
        OnPropertyChanged(nameof(HasNoVisibleClients));
    }

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);
}
