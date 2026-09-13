using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel vm)
            await vm.TryAutoConnectAsync();
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
