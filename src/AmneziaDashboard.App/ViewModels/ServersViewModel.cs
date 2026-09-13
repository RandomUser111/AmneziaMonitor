using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class ServersViewModel : ViewModelBase
{
    public static string PasswordRequiredMessage => LocalizationService.T("SSH password is required.", "Требуется ввод SSH-пароля.");

    private readonly DashboardViewModel _dashboard;
    private readonly IServerProfileStore _profileStore;
    private readonly ISecretStore _secretStore;
    private readonly IServerProbeService _probeService;
    private readonly AppEventLogService? _eventLog;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentServerName))]
    private string? _currentServerProfileId;

    public ObservableCollection<ServerProfileItemViewModel> Servers { get; } = [];

    public bool HasServers => Servers.Count > 0;

    public bool HasNoServers => Servers.Count == 0;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string CurrentServerName =>
        Servers.FirstOrDefault(x => x.IsCurrent)?.Name ?? LocalizationService.T("No server selected", "Сервер не выбран");

    public bool SecureStorageAvailable => _secretStore.IsAvailable;

    public string SecureStorageBackendName => _secretStore.BackendName;

    public ServersViewModel(
        DashboardViewModel dashboard,
        IServerProfileStore profileStore,
        ISecretStore secretStore,
        IServerProbeService probeService,
        AppEventLogService? eventLog = null)
    {
        _dashboard = dashboard;
        _profileStore = profileStore;
        _secretStore = secretStore;
        _probeService = probeService;
        _eventLog = eventLog;
        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
    }


    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Servers)
            item.NotifyLocalizationChanged();

        OnPropertyChanged(nameof(CurrentServerName));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _profileStore.LoadAsync(cancellationToken);

        Servers.Clear();
        foreach (var profile in profiles
                     .OrderByDescending(x => x.LastUsedAt)
                     .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Servers.Add(new ServerProfileItemViewModel(
                profile,
                profile.Id == CurrentServerProfileId));
        }

        NotifyCollectionStateChanged();
    }

    public async Task<OperationResult> ConnectAsync(
        ServerProfileItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return OperationResult.Fail(LocalizationService.T("Wait for the current connection attempt to finish.", "Подождите завершения текущего подключения."));

        if (item.IsCurrent)
            return OperationResult.Ok(LocalizationService.T("This server is already connected.", "Этот сервер уже подключён."));

        string? password = null;
        if (item.Profile.RememberPassword && _secretStore.IsAvailable)
        {
            password = await _secretStore.GetPasswordAsync(item.Profile.Id, cancellationToken);
        }

        if (string.IsNullOrEmpty(password))
        {
            StatusMessage = PasswordRequiredMessage;
            return OperationResult.Fail(PasswordRequiredMessage);
        }

        return await ConnectWithPasswordAsync(item.Profile, password, cancellationToken);
    }

    public async Task<OperationResult> ConnectWithPasswordAsync(
        ServerProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return OperationResult.Fail(LocalizationService.T("Wait for the current connection attempt to finish.", "Подождите завершения текущего подключения."));

        if (string.IsNullOrEmpty(password))
        {
            StatusMessage = PasswordRequiredMessage;
            return OperationResult.Fail(PasswordRequiredMessage);
        }

        IsBusy = true;
        StatusMessage = LocalizationService.T($"Connecting to \"{profile.Name}\"…", $"Подключение к «{profile.Name}»…");

        try
        {
            var connection = new ServerConnection
            {
                Name = profile.Name,
                Host = profile.Host,
                Port = profile.Port,
                Username = profile.Username,
                Password = password
            };

            var probe = await _probeService.ProbeAsync(connection, cancellationToken);
            if (!probe.Success)
            {
                StatusMessage = string.IsNullOrWhiteSpace(probe.ErrorMessage)
                    ? LocalizationService.T("Could not connect to the server.", "Не удалось подключиться к серверу.")
                    : LocalizationService.TranslateExternalMessage(probe.ErrorMessage);
                _eventLog?.Error("SSH", LocalizationService.T($"Could not connect to {profile.Name}: {StatusMessage}", $"Не удалось подключиться к {profile.Name}: {StatusMessage}"));
                return OperationResult.Fail(StatusMessage);
            }

            profile.LastUsedAt = DateTimeOffset.UtcNow;
            await _profileStore.SaveAsync(profile, cancellationToken);

            _dashboard.ApplyConnection(connection, probe);
            SetCurrentProfile(profile.Id);
            await RefreshAsync(cancellationToken);

            StatusMessage = LocalizationService.T($"Connected: {profile.Name} ({profile.Host}:{profile.Port}).", $"Подключено: {profile.Name} ({profile.Host}:{profile.Port}).");
            return OperationResult.Ok(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = LocalizationService.T("Connection cancelled.", "Подключение отменено.");
            return OperationResult.Fail(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.T($"Error: {ex.Message}", $"Ошибка: {ex.Message}");
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AcceptConnectionAsync(
        ServerConnection connection,
        ServerProbeResult probe,
        CancellationToken cancellationToken = default)
    {
        _dashboard.ApplyConnection(connection, probe);

        var profiles = await _profileStore.LoadAsync(cancellationToken);
        var matchingProfile = profiles.FirstOrDefault(x =>
            x.Host.Equals(connection.Host, StringComparison.OrdinalIgnoreCase) &&
            x.Port == connection.Port &&
            x.Username.Equals(connection.Username, StringComparison.OrdinalIgnoreCase));

        if (matchingProfile is not null)
        {
            matchingProfile.LastUsedAt = DateTimeOffset.UtcNow;
            await _profileStore.SaveAsync(matchingProfile, cancellationToken);
            SetCurrentProfile(matchingProfile.Id);
        }
        else
        {
            SetCurrentProfile(null);
        }

        await RefreshAsync(cancellationToken);
        StatusMessage = LocalizationService.T($"Connected: {connection.Name} ({connection.Host}:{connection.Port}).", $"Подключено: {connection.Name} ({connection.Host}:{connection.Port}).");
    }

    public async Task<OperationResult> DeleteAsync(
        ServerProfileItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        if (item.IsCurrent)
        {
            StatusMessage = LocalizationService.T("You cannot delete the server Monitor is currently connected to. Switch to another server first.", "Нельзя удалить сервер, к которому сейчас подключён Monitor. Сначала переключитесь на другой сервер.");
            return OperationResult.Fail(StatusMessage);
        }

        try
        {
            await _secretStore.DeletePasswordAsync(item.Profile.Id, cancellationToken);
            await _profileStore.DeleteAsync(item.Profile.Id, cancellationToken);
            await RefreshAsync(cancellationToken);
            StatusMessage = LocalizationService.T($"Server \"{item.Name}\" deleted.", $"Сервер «{item.Name}» удалён.");
            _eventLog?.Info("Servers", StatusMessage);
            return OperationResult.Ok(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.T($"Could not delete server: {ex.Message}", $"Не удалось удалить сервер: {ex.Message}");
            _eventLog?.Error("Servers", StatusMessage);
            return OperationResult.Fail(ex.Message);
        }
    }

    public ServerProfileItemViewModel? FindById(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        return Servers.FirstOrDefault(x => x.Id == profileId);
    }

    public ServerProfileItemViewModel? FindCurrent() =>
        Servers.FirstOrDefault(x => x.IsCurrent);

    public void SetCurrentProfile(string? profileId)
    {
        CurrentServerProfileId = profileId;

        foreach (var item in Servers)
            item.IsCurrent = item.Id == profileId;

        OnPropertyChanged(nameof(CurrentServerName));
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(HasNoServers));
        OnPropertyChanged(nameof(CurrentServerName));
    }
}
