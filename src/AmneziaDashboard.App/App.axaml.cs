using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.App.ViewModels;
using AmneziaDashboard.App.Views;

namespace AmneziaDashboard.App;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showMenuItem;
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _serversMenuItem;
    private NativeMenuItem? _exitMenuItem;
    private MainWindowViewModel? _mainViewModel;
    private MainWindow? _mainWindow;

    public static bool IsExplicitExit { get; private set; }
    public static bool TrayAvailable { get; private set; }

    public override void Initialize()
    {
        StartupDiagnostics.Write("Avalonia application initialization started.");
        AvaloniaXamlLoader.Load(this);
        LocalizationService.ApplyLanguage(AppPreferenceStore.LoadLanguage(), raiseEvent: false);
        ApplySavedTheme();
        StartupDiagnostics.Write("Avalonia application initialization completed.");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _mainViewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            _mainWindow = mainWindow;

            desktop.MainWindow = mainWindow;
            CreateTrayIcon(mainWindow, desktop);
            _mainViewModel.Dashboard.PropertyChanged += (_, _) => UpdateTrayText();
            _mainViewModel.Servers.CollectionChanged += (_, _) => RebuildTrayServers();
            LocalizationService.LanguageChanged += (_, _) => UpdateTrayText();
            StartupDiagnostics.Write("Main window and system tray created.");
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ExitApplication()
    {
        IsExplicitExit = true;
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void CreateTrayIcon(MainWindow mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _showMenuItem = new NativeMenuItem(LocalizationService.T("Open Amnezia Monitor", "Открыть Amnezia Monitor"));
            _showMenuItem.Click += (_, _) => ShowMainWindow(mainWindow);

            _statusMenuItem = new NativeMenuItem { IsEnabled = false };
            _serversMenuItem = new NativeMenuItem(LocalizationService.T("Servers", "Серверы")) { Menu = new NativeMenu() };

            _exitMenuItem = new NativeMenuItem(LocalizationService.T("Exit", "Выход"));
            _exitMenuItem.Click += (_, _) => ExitApplication();

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AmneziaMonitor/Assets/amnezia-monitor-icon.ico"))),
                ToolTipText = "Amnezia Monitor",
                Menu = new NativeMenu
                {
                    _showMenuItem,
                    _statusMenuItem,
                    _serversMenuItem,
                    new NativeMenuItemSeparator(),
                    _exitMenuItem
                }
            };

            _trayIcon.Clicked += (_, _) => ShowMainWindow(mainWindow);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
            TrayAvailable = true;
            UpdateTrayText();
            RebuildTrayServers();
        }
        catch (Exception ex)
        {
            TrayAvailable = false;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            StartupDiagnostics.Write($"Tray initialization failed: {ex}");
        }
    }

    private static void ShowMainWindow(MainWindow mainWindow)
    {
        mainWindow.ShowInTaskbar = true;
        if (!mainWindow.IsVisible)
            mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void UpdateTrayText()
    {
        if (_showMenuItem is not null)
            _showMenuItem.Header = LocalizationService.T("Open Amnezia Monitor", "Открыть Amnezia Monitor");
        if (_serversMenuItem is not null)
            _serversMenuItem.Header = LocalizationService.T("Servers", "Серверы");
        if (_statusMenuItem is not null)
        {
            var status = _mainViewModel?.Dashboard.ServerStatus ?? LocalizationService.T("Not connected", "Не подключён");
            _statusMenuItem.Header = LocalizationService.T($"Status: {status}", $"Статус: {status}");
        }
        if (_exitMenuItem is not null)
            _exitMenuItem.Header = LocalizationService.T("Exit", "Выход");
        RebuildTrayServers();
    }

    private void RebuildTrayServers()
    {
        if (_serversMenuItem is null || _mainViewModel is null)
            return;

        var menu = new NativeMenu();
        foreach (var server in _mainViewModel.Servers)
        {
            var label = server.Id == _mainViewModel.CurrentServerProfileId
                ? $"● {server.Name}"
                : server.Name;
            var item = new NativeMenuItem(label);
            item.Click += async (_, _) =>
            {
                var result = await _mainViewModel.ConnectToServerAsync(server);
                if (!result.Success)
                {
                    if (_mainWindow is not null)
                    {
                        ShowMainWindow(_mainWindow);
                        _mainViewModel.ShowServersPage();
                    }
                }
                UpdateTrayText();
            };
            menu.Items.Add(item);
        }

        if (_mainViewModel.Servers.Count == 0)
            menu.Items.Add(new NativeMenuItem(LocalizationService.T("No saved servers", "Нет сохранённых серверов")) { IsEnabled = false });

        _serversMenuItem.Menu = menu;
    }

    private void ApplySavedTheme()
    {
        RequestedThemeVariant = AppPreferenceStore.LoadTheme() switch
        {
            "Dark" => ThemeVariant.Dark,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
    }
}
