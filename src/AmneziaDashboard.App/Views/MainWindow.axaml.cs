using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class MainWindow : Window
{
    private bool _handlingMinimize;

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += MainWindow_PropertyChanged;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.TryAutoConnectAsync();
            _ = vm.CheckForUpdatesOnStartupAsync();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var systemShutdown = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (App.TrayAvailable && !App.IsExplicitExit && !systemShutdown)
        {
            if (AppPreferenceStore.LoadCloseToTray())
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                e.Cancel = true;
                App.ExitApplication();
                return;
            }
        }

        base.OnClosing(e);
    }

    private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!App.TrayAvailable || _handlingMinimize || e.Property != WindowStateProperty || !AppPreferenceStore.LoadMinimizeToTray())
            return;

        if (WindowState != WindowState.Minimized)
            return;

        _handlingMinimize = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                HideToTray();
            }
            finally
            {
                _handlingMinimize = false;
            }
        });
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private async void QuickServer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.IsSynchronizingQuickSelection ||
            QuickServerComboBox.SelectedItem is not ServerProfileItemViewModel item ||
            item.Id == vm.CurrentServerProfileId)
        {
            return;
        }

        var result = await vm.ConnectToServerAsync(item);
        if (result.Success)
            return;

        if (result.ErrorMessage == ServersViewModel.PasswordRequiredMessage)
        {
            var dialog = new ConnectServerWindow(item.Id, showSavedProfilePicker: false);
            var confirmed = await dialog.ShowDialog<bool>(this);

            if (confirmed && dialog.Connection is not null && dialog.ProbeResult is not null)
                await vm.AcceptConnectionAsync(dialog.Connection, dialog.ProbeResult);

            return;
        }

        vm.ShowServersPage();
    }
}
