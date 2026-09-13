using Avalonia;
using AmneziaDashboard.App.Services;
using System;
using System.Threading.Tasks;

namespace AmneziaDashboard.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupDiagnostics.Write("Process started.");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                StartupDiagnostics.WriteException("AppDomain.UnhandledException", ex);
            else
                StartupDiagnostics.Write($"FATAL [AppDomain.UnhandledException] {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupDiagnostics.WriteException("TaskScheduler.UnobservedTaskException", e.Exception);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            StartupDiagnostics.Write("Application lifetime ended normally.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("Main", ex);
            StartupDiagnostics.ShowFatalError(ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
