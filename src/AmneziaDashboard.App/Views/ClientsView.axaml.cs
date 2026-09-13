using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class ClientsView : UserControl
{
    public ClientsView()
    {
        InitializeComponent();
    }


    private async void CreateClient_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var protocols = vm.GetCreatableProtocols();
        if (protocols.Count == 0)
            return;

        var dialog = new CreateClientWindow(protocols);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        var result = await vm.CreateClientAsync(dialog.ContainerName, dialog.ClientName);
        if (!result.Success)
            return;

        var configWindow = new ClientConfigWindow(
            dialog.ClientName,
            result.ClientAddress,
            result.ConfigText);

        await configWindow.ShowDialog(owner);
    }

    private async void RenameClient_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not VpnClientViewModel client)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new RenameClientWindow(client.Name);
        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        await vm.RenameClientAsync(client, dialog.NewName);
    }

    private async void RestoreClientConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not VpnClientViewModel client)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new RestoreClientConfigWindow(
            client.Name,
            client.Protocol,
            client.Address);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        var result = await vm.RestoreClientConfigAsync(client);
        if (!result.Success)
            return;

        var configWindow = new ClientConfigWindow(
            client.Name,
            result.ClientAddress,
            result.ConfigText,
            isRestored: true);

        await configWindow.ShowDialog(owner);
    }

    private async void RevokeClient_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not VpnClientViewModel client)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var dialog = new RevokeClientWindow(
            client.Name,
            client.Protocol,
            client.Address);

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        await vm.RevokeClientAsync(client);
    }
}
