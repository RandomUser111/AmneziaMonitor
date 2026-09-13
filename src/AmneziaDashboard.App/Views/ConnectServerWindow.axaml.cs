using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using AmneziaDashboard.App.ViewModels;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.Infrastructure.Security;
using AmneziaDashboard.Infrastructure.Ssh;
using AmneziaDashboard.Infrastructure.Storage;

namespace AmneziaDashboard.App.Views;

public partial class ConnectServerWindow : Window
{
    private readonly IServerProbeService _serverProbeService;
    private readonly IServerProfileStore _profileStore;
    private readonly ISecretStore _secretStore;
    private readonly string? _initialProfileId;
    private readonly bool _showSavedProfilePicker;

    public ServerConnection? Connection { get; private set; }

    public ServerProbeResult? ProbeResult { get; private set; }

    public ConnectServerWindow()
        : this(
            initialProfileId: null,
            showSavedProfilePicker: true,
            new SshServerProbeService(),
            new JsonServerProfileStore(),
            new CrossPlatformSecretStore())
    {
    }

    public ConnectServerWindow(
        string? initialProfileId,
        bool showSavedProfilePicker)
        : this(
            initialProfileId,
            showSavedProfilePicker,
            new SshServerProbeService(),
            new JsonServerProfileStore(),
            new CrossPlatformSecretStore())
    {
    }

    public ConnectServerWindow(
        IServerProbeService serverProbeService,
        IServerProfileStore profileStore,
        ISecretStore secretStore)
        : this(
            initialProfileId: null,
            showSavedProfilePicker: true,
            serverProbeService,
            profileStore,
            secretStore)
    {
    }

    public ConnectServerWindow(
        string? initialProfileId,
        bool showSavedProfilePicker,
        IServerProbeService serverProbeService,
        IServerProfileStore profileStore,
        ISecretStore secretStore)
    {
        InitializeComponent();

        _initialProfileId = initialProfileId;
        _showSavedProfilePicker = showSavedProfilePicker;
        _serverProbeService = serverProbeService;
        _profileStore = profileStore;
        _secretStore = secretStore;

        var vm = new ConnectServerViewModel
        {
            ShowSavedProfiles = showSavedProfilePicker,
            ShowSaveProfileOption = showSavedProfilePicker,
            SaveProfile = true
        };

        Height = showSavedProfilePicker ? 690 : 610;

        if (!showSavedProfilePicker)
        {
            if (string.IsNullOrWhiteSpace(initialProfileId))
            {
                vm.HeaderText = "Добавить сервер";
                vm.DescriptionText = "Введите SSH-данные нового Amnezia VPN сервера.";
                vm.PrimaryButtonText = "Добавить и подключиться";
            }
            else
            {
                vm.HeaderText = "Изменить сервер";
                vm.DescriptionText = "Измените параметры сервера и подключитесь к нему.";
                vm.PrimaryButtonText = "Сохранить и подключиться";
            }
        }

        vm.SetSecureStorage(_secretStore.IsAvailable, _secretStore.BackendName);
        Title = vm.HeaderText;
        vm.PropertyChanged += ViewModelOnPropertyChanged;

        DataContext = vm;
        Opened += async (_, _) => await LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        if (DataContext is not ConnectServerViewModel vm)
            return;

        try
        {
            var profiles = await _profileStore.LoadAsync();

            if (_showSavedProfilePicker)
            {
                vm.SetProfiles(profiles);
                vm.ShowSavedProfiles = profiles.Count > 0;
            }
            else if (!string.IsNullOrWhiteSpace(_initialProfileId))
            {
                var selected = profiles.FirstOrDefault(x => x.Id == _initialProfileId);
                vm.SetProfiles(selected is null ? [] : [selected]);
            }
            else
            {
                vm.SetProfiles([]);
                vm.Name = "Мой сервер";
                vm.Host = string.Empty;
                vm.Port = 22;
                vm.Username = "root";
                vm.Password = string.Empty;
                vm.SaveProfile = true;
                vm.RememberPassword = false;
                vm.AutoConnect = false;
            }

            await LoadSelectedPasswordAsync(vm);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Не удалось загрузить сохранённые серверы: {ex.Message}";
        }
    }

    private async void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectServerViewModel.SelectedProfile) ||
            sender is not ConnectServerViewModel vm)
        {
            return;
        }

        await LoadSelectedPasswordAsync(vm);
    }

    private async Task LoadSelectedPasswordAsync(ConnectServerViewModel vm)
    {
        var profile = vm.SelectedProfile;
        if (profile is null || !profile.RememberPassword || !_secretStore.IsAvailable)
        {
            vm.Password = string.Empty;
            return;
        }

        try
        {
            var profileId = profile.Id;
            var password = await _secretStore.GetPasswordAsync(profileId);

            if (vm.SelectedProfile?.Id != profileId)
                return;

            vm.Password = password ?? string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                vm.StatusMessage =
                    "Для сохранённого сервера пароль не найден в защищённом хранилище. Введите его снова.";
            }
        }
        catch (Exception ex)
        {
            vm.Password = string.Empty;
            vm.StatusMessage = $"Не удалось прочитать сохранённый пароль: {ex.Message}";
        }
    }

    private async void ConnectButton_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConnectServerViewModel vm)
            return;

        if (string.IsNullOrWhiteSpace(vm.Host))
        {
            vm.StatusMessage = "Введите IP-адрес или имя сервера.";
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.Username))
        {
            vm.StatusMessage = "Введите имя пользователя.";
            return;
        }

        if (string.IsNullOrEmpty(vm.Password))
        {
            vm.StatusMessage = "Введите SSH-пароль.";
            return;
        }

        vm.IsConnecting = true;
        vm.StatusMessage = "Подключение к серверу...";

        try
        {
            var connection = new ServerConnection
            {
                Name = string.IsNullOrWhiteSpace(vm.Name) ? vm.Host.Trim() : vm.Name.Trim(),
                Host = vm.Host.Trim(),
                Port = vm.Port,
                Username = vm.Username.Trim(),
                Password = vm.Password
            };

            var result = await _serverProbeService.ProbeAsync(connection);

            if (!result.Success)
            {
                vm.StatusMessage = result.ErrorMessage;
                return;
            }

            if (vm.SaveProfile)
            {
                var matchingProfile = vm.SelectedProfile ?? vm.SavedProfiles.FirstOrDefault(x =>
                    x.Host.Equals(connection.Host, StringComparison.OrdinalIgnoreCase) &&
                    x.Port == connection.Port &&
                    x.Username.Equals(connection.Username, StringComparison.OrdinalIgnoreCase));

                if (matchingProfile is null && !string.IsNullOrWhiteSpace(_initialProfileId))
                {
                    var profiles = await _profileStore.LoadAsync();
                    matchingProfile = profiles.FirstOrDefault(x => x.Id == _initialProfileId);
                }

                var profile = new ServerProfile
                {
                    Id = matchingProfile?.Id ?? Guid.NewGuid().ToString("N"),
                    Name = connection.Name,
                    Host = connection.Host,
                    Port = connection.Port,
                    Username = connection.Username,
                    RememberPassword = vm.RememberPassword && _secretStore.IsAvailable,
                    AutoConnect = vm.AutoConnect && vm.RememberPassword && _secretStore.IsAvailable,
                    LastUsedAt = DateTimeOffset.UtcNow
                };

                if (profile.RememberPassword)
                {
                    var secretResult = await _secretStore.SetPasswordAsync(profile.Id, connection.Password);
                    if (!secretResult.Success)
                    {
                        profile.RememberPassword = false;
                        profile.AutoConnect = false;
                        vm.StatusMessage =
                            $"Соединение установлено, но пароль не удалось сохранить: {secretResult.ErrorMessage}";
                    }
                }
                else
                {
                    await _secretStore.DeletePasswordAsync(profile.Id);
                }

                if (profile.AutoConnect)
                {
                    var existingProfiles = await _profileStore.LoadAsync();
                    foreach (var other in existingProfiles.Where(x => x.Id != profile.Id && x.AutoConnect))
                    {
                        other.AutoConnect = false;
                        await _profileStore.SaveAsync(other);
                    }
                }

                await _profileStore.SaveAsync(profile);
            }

            Connection = connection;
            ProbeResult = result;

            if (string.IsNullOrWhiteSpace(vm.StatusMessage) ||
                vm.StatusMessage == "Подключение к серверу...")
            {
                vm.StatusMessage = "Соединение установлено.";
            }

            Close(true);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            vm.IsConnecting = false;
        }
    }

    private async void DeleteProfileButton_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ConnectServerViewModel vm ||
            vm.SelectedProfile is null)
        {
            return;
        }

        try
        {
            var profileId = vm.SelectedProfile.Id;
            await _secretStore.DeletePasswordAsync(profileId);
            await _profileStore.DeleteAsync(profileId);
            vm.StatusMessage = "Сохранённый сервер и его пароль удалены.";
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Не удалось удалить сохранённый сервер: {ex.Message}";
        }
    }

    private void CancelButton_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
