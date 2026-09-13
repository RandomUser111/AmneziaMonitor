using Avalonia;
using Avalonia.Styling;
using AmneziaDashboard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmneziaDashboard.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentTheme;

    public SettingsViewModel()
    {
        var savedTheme = ThemePreferenceStore.LoadTheme();
        _currentTheme = savedTheme switch
        {
            "Dark" => "Тёмная",
            "System" => "Как в системе",
            _ => "Светлая"
        };
    }

    [RelayCommand]
    private void SetLightTheme()
    {
        ApplyTheme(ThemeVariant.Light, "Светлая", "Light");
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        ApplyTheme(ThemeVariant.Dark, "Тёмная", "Dark");
    }

    [RelayCommand]
    private void SetSystemTheme()
    {
        ApplyTheme(ThemeVariant.Default, "Как в системе", "System");
    }

    private void ApplyTheme(ThemeVariant theme, string name, string storedValue)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = theme;

        ThemePreferenceStore.SaveTheme(storedValue);
        CurrentTheme = name;
    }
}
