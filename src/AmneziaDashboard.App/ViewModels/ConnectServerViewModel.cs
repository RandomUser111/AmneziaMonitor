using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using AmneziaDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class ConnectServerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = "Мой сервер";

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = "root";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ServerProfile? _selectedProfile;

    [ObservableProperty]
    private bool _saveProfile = true;

    [ObservableProperty]
    private bool _rememberPassword;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private bool _secureStorageAvailable;

    [ObservableProperty]
    private string _secureStorageText = "Защищённое хранилище недоступно";

    [ObservableProperty]
    private bool _hasSavedProfiles;

    [ObservableProperty]
    private bool _showSavedProfiles = true;

    [ObservableProperty]
    private bool _showSaveProfileOption = true;

    [ObservableProperty]
    private string _headerText = "Подключить сервер";

    [ObservableProperty]
    private string _descriptionText = "Введите данные SSH или выберите сохранённый сервер.";

    [ObservableProperty]
    private string _primaryButtonText = "Подключиться";

    public ObservableCollection<ServerProfile> SavedProfiles { get; } = [];

    public bool CanDeleteProfile => SelectedProfile is not null;

    public void SetSecureStorage(bool available, string backendName)
    {
        SecureStorageAvailable = available;
        SecureStorageText = available
            ? $"Пароль будет сохранён в {backendName}."
            : "Защищённое хранилище недоступно; пароль сохраняться не будет.";

        if (!available)
        {
            RememberPassword = false;
            AutoConnect = false;
        }
    }

    public void SetProfiles(IEnumerable<ServerProfile> profiles)
    {
        SavedProfiles.Clear();

        foreach (var profile in profiles
                     .OrderByDescending(x => x.LastUsedAt)
                     .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            SavedProfiles.Add(profile);
        }

        HasSavedProfiles = SavedProfiles.Count > 0;
        SelectedProfile = SavedProfiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(ServerProfile? value)
    {
        OnPropertyChanged(nameof(CanDeleteProfile));

        if (value is null)
            return;

        Name = value.Name;
        Host = value.Host;
        Port = value.Port;
        Username = value.Username;
        RememberPassword = SecureStorageAvailable && value.RememberPassword;
        AutoConnect = RememberPassword && value.AutoConnect;
        Password = string.Empty;
    }

    partial void OnRememberPasswordChanged(bool value)
    {
        if (!value && AutoConnect)
            AutoConnect = false;
    }

    partial void OnAutoConnectChanged(bool value)
    {
        if (value && SecureStorageAvailable && !RememberPassword)
            RememberPassword = true;
    }
}
