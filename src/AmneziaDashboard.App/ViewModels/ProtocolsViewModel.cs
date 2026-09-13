using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;

namespace AmneziaDashboard.App.ViewModels;

public class ProtocolsViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboard;
    private readonly IProtocolManagementService _protocolManagementService;
    private readonly AppEventLogService? _eventLog;
    private bool _isBusy;
    private string _operationStatus = string.Empty;

    public ProtocolsViewModel(
        DashboardViewModel dashboard,
        IProtocolManagementService protocolManagementService,
        AppEventLogService? eventLog = null)
    {
        _dashboard = dashboard;
        _protocolManagementService = protocolManagementService;
        _eventLog = eventLog;
        _dashboard.PropertyChanged += DashboardOnPropertyChanged;
    }

    public ObservableCollection<ProtocolStatusViewModel> Protocols =>
        _dashboard.Protocols;

    public string ServerStatus =>
        _dashboard.ServerStatus;

    public string OperatingSystem =>
        _dashboard.OperatingSystem;

    public string DockerVersion =>
        _dashboard.DockerVersion;

    public bool HasProtocols =>
        _dashboard.HasProtocols;

    public bool HasNoProtocols =>
        _dashboard.HasNoProtocols;

    public string ProtocolsMessage =>
        _dashboard.ProtocolsMessage;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public string OperationStatus
    {
        get => _operationStatus;
        private set
        {
            if (_operationStatus == value)
                return;

            _operationStatus = value;
            OnPropertyChanged(nameof(OperationStatus));
            OnPropertyChanged(nameof(HasOperationStatus));
        }
    }

    public bool HasOperationStatus =>
        !string.IsNullOrWhiteSpace(OperationStatus);

    public async Task<OperationResult> StartProtocolAsync(ProtocolStatusViewModel protocol)
    {
        return await RunOperationAsync(protocol, (connection, token) =>
            _protocolManagementService.StartContainerAsync(
                connection,
                protocol.ContainerName,
                token));
    }

    public async Task<OperationResult> StopProtocolAsync(ProtocolStatusViewModel protocol)
    {
        return await RunOperationAsync(protocol, (connection, token) =>
            _protocolManagementService.StopContainerAsync(
                connection,
                protocol.ContainerName,
                token));
    }

    public async Task<OperationResult> RestartProtocolAsync(ProtocolStatusViewModel protocol)
    {
        return await RunOperationAsync(protocol, (connection, token) =>
            _protocolManagementService.RestartContainerAsync(
                connection,
                protocol.ContainerName,
                token));
    }

    private async Task<OperationResult> RunOperationAsync(
        ProtocolStatusViewModel protocol,
        Func<ServerConnection, CancellationToken, Task<OperationResult>> operation)
    {
        if (IsBusy)
            return OperationResult.Fail("Подождите завершения текущей операции.");

        var connection = _dashboard.CurrentConnection;
        if (connection is null)
        {
            OperationStatus = "Сначала подключитесь к серверу.";
            return OperationResult.Fail("Нет активного подключения к серверу.");
        }

        IsBusy = true;
        OperationStatus = $"Выполняется операция для {protocol.DisplayName}…";

        try
        {
            var result = await operation(connection, CancellationToken.None);

            OperationStatus = result.Success
                ? string.IsNullOrWhiteSpace(result.Message)
                    ? $"Операция для {protocol.DisplayName} выполнена. Мониторинг скоро обновит статус."
                    : result.Message
                : $"Ошибка: {result.ErrorMessage}";

            if (result.Success)
                _eventLog?.Success("Протоколы", $"{protocol.DisplayName}: {OperationStatus}");
            else
                _eventLog?.Error("Протоколы", $"{protocol.DisplayName}: {result.ErrorMessage}");

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DashboardOnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.ServerStatus))
            OnPropertyChanged(nameof(ServerStatus));

        if (e.PropertyName is nameof(DashboardViewModel.OperatingSystem))
            OnPropertyChanged(nameof(OperatingSystem));

        if (e.PropertyName is nameof(DashboardViewModel.DockerVersion))
            OnPropertyChanged(nameof(DockerVersion));

        if (e.PropertyName is nameof(DashboardViewModel.HasProtocols))
            OnPropertyChanged(nameof(HasProtocols));

        if (e.PropertyName is nameof(DashboardViewModel.HasNoProtocols))
            OnPropertyChanged(nameof(HasNoProtocols));

        if (e.PropertyName is nameof(DashboardViewModel.ProtocolsMessage))
            OnPropertyChanged(nameof(ProtocolsMessage));
    }
}
