using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmneziaDashboard.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppUpdateService _updateService;

    [ObservableProperty]
    private string _currentTheme = string.Empty;

    [ObservableProperty]
    private string _currentLanguage = string.Empty;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _automaticUpdateChecks;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    public bool CanOpenUpdate => IsUpdateAvailable && !string.IsNullOrWhiteSpace(_updateService.ReleaseUrl);
    public bool CanCheckForUpdates => !IsCheckingUpdates;

    public SettingsViewModel()
        : this(new AppUpdateService(new DesktopNotificationService()))
    {
    }

    public SettingsViewModel(AppUpdateService updateService)
    {
        _updateService = updateService;
        _notificationsEnabled = AppPreferenceStore.LoadNotificationsEnabled();
        _closeToTray = AppPreferenceStore.LoadCloseToTray();
        _minimizeToTray = AppPreferenceStore.LoadMinimizeToTray();
        _automaticUpdateChecks = AppPreferenceStore.LoadAutomaticUpdateChecks();
        RefreshLocalizedLabels();
        RefreshUpdateState();

        LocalizationService.LanguageChanged += LocalizationServiceOnLanguageChanged;
        _updateService.StateChanged += (_, _) => RefreshUpdateState();
    }

    partial void OnNotificationsEnabledChanged(bool value) => AppPreferenceStore.SaveNotificationsEnabled(value);
    partial void OnCloseToTrayChanged(bool value) => AppPreferenceStore.SaveCloseToTray(value);
    partial void OnMinimizeToTrayChanged(bool value) => AppPreferenceStore.SaveMinimizeToTray(value);
    partial void OnAutomaticUpdateChecksChanged(bool value) => AppPreferenceStore.SaveAutomaticUpdateChecks(value);

    [RelayCommand]
    private void SetLightTheme() => ApplyTheme(ThemeVariant.Light, "Light");

    [RelayCommand]
    private void SetDarkTheme() => ApplyTheme(ThemeVariant.Dark, "Dark");

    [RelayCommand]
    private void SetSystemTheme() => ApplyTheme(ThemeVariant.Default, "System");

    [RelayCommand]
    private void SetEnglishLanguage() => ApplyLanguage(LocalizationService.English);

    [RelayCommand]
    private void SetRussianLanguage() => ApplyLanguage(LocalizationService.Russian);

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await _updateService.CheckAsync(force: true);
        RefreshUpdateState();
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        _updateService.OpenReleasePage();
    }

    private void ApplyTheme(ThemeVariant theme, string storedValue)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = theme;

        AppPreferenceStore.SaveTheme(storedValue);
        RefreshLocalizedLabels();
    }

    private void ApplyLanguage(string language)
    {
        AppPreferenceStore.SaveLanguage(language);
        LocalizationService.ApplyLanguage(language);
        RefreshLocalizedLabels();
        RefreshUpdateState();
    }

    private void LocalizationServiceOnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedLabels();
        RefreshUpdateState();
    }

    private void RefreshLocalizedLabels()
    {
        CurrentTheme = AppPreferenceStore.LoadTheme() switch
        {
            "Dark" => LocalizationService.T("Dark", "Тёмная"),
            "System" => LocalizationService.T("System", "Как в системе"),
            _ => LocalizationService.T("Light", "Светлая")
        };

        CurrentLanguage = LocalizationService.IsRussian ? "Русский" : "English";
    }

    private void RefreshUpdateState()
    {
        IsCheckingUpdates = _updateService.IsChecking;
        IsUpdateAvailable = _updateService.IsUpdateAvailable;
        UpdateStatus = _updateService.GetStatusText();
        OnPropertyChanged(nameof(CanOpenUpdate));
        OnPropertyChanged(nameof(CanCheckForUpdates));
    }
}
