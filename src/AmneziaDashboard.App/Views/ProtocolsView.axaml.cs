using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class ProtocolsView : UserControl
{
    public ProtocolsView()
    {
        InitializeComponent();
    }

    private async void StartProtocol_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProtocolsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ProtocolStatusViewModel protocol)
        {
            return;
        }

        await vm.StartProtocolAsync(protocol);
    }

    private async void StopProtocol_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProtocolsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ProtocolStatusViewModel protocol)
        {
            return;
        }

        await vm.StopProtocolAsync(protocol);
    }

    private async void RestartProtocol_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProtocolsViewModel vm ||
            sender is not Button button ||
            button.DataContext is not ProtocolStatusViewModel protocol)
        {
            return;
        }

        await vm.RestartProtocolAsync(protocol);
    }
}
