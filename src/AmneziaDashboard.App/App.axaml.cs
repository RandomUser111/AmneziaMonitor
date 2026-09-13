using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.App.ViewModels;
using AmneziaDashboard.App.Views;

namespace AmneziaDashboard.App;

public partial class App : Application
{
    public override void Initialize()
    {
        StartupDiagnostics.Write("Avalonia application initialization started.");
        AvaloniaXamlLoader.Load(this);
        ApplySavedTheme();
        StartupDiagnostics.Write("Avalonia application initialization completed.");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            StartupDiagnostics.Write("Main window created.");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplySavedTheme()
    {
        RequestedThemeVariant = ThemePreferenceStore.LoadTheme() switch
        {
            "Dark" => ThemeVariant.Dark,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
    }
}
