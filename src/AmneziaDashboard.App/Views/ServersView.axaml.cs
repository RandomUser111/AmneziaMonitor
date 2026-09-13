using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class ServersView : UserControl
{
    public ServersView()
    {
        InitializeComponent();
    }

    private async void AddServer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ServersViewModel vm)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new ConnectServerWindow(initialProfileId: null, showSavedProfilePicker: false);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed || dialog.Connection is null || dialog.ProbeResult is null)
            return;

        await vm.AcceptConnectionAsync(dialog.Connection, dialog.ProbeResult);
    }

    private async void ConnectServer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ServersViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ServerProfileItemViewModel item)
        {
            return;
        }

        var result = await vm.ConnectAsync(item);
        if (result.Success || result.ErrorMessage != ServersViewModel.PasswordRequiredMessage)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new ConnectServerWindow(item.Id, showSavedProfilePicker: false);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed || dialog.Connection is null || dialog.ProbeResult is null)
            return;

        await vm.AcceptConnectionAsync(dialog.Connection, dialog.ProbeResult);
    }

    private async void EditServer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ServersViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ServerProfileItemViewModel item)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new ConnectServerWindow(item.Id, showSavedProfilePicker: false);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed || dialog.Connection is null || dialog.ProbeResult is null)
        {
            await vm.RefreshAsync();
            return;
        }

        await vm.AcceptConnectionAsync(dialog.Connection, dialog.ProbeResult);
    }

    private async void DeleteServer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ServersViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ServerProfileItemViewModel item)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        if (item.IsCurrent)
        {
            await vm.DeleteAsync(item);
            return;
        }

        var confirm = new DeleteServerWindow(item.Name, item.AddressText);
        var confirmed = await confirm.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        await vm.DeleteAsync(item);
    }
}
