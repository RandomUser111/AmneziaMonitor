using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

using AmneziaDashboard.App.Services;
namespace AmneziaDashboard.App.Views;

public partial class CreateClientWindow : Window
{
    public string ClientName { get; private set; } = string.Empty;

    public string ContainerName { get; private set; } = string.Empty;

    public CreateClientWindow()
        : this([])
    {
    }

    public CreateClientWindow(IReadOnlyList<ProtocolStatusViewModel> protocols)
    {
        InitializeComponent();

        ProtocolBox.ItemsSource = protocols;
        ProtocolBox.SelectedIndex = protocols.Count > 0 ? 0 : -1;
    }

    private void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = ClientNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationText.Text = LocalizationService.T("Enter a client name.", "Введите имя клиента.");
            return;
        }

        if (ProtocolBox.SelectedItem is not ProtocolStatusViewModel protocol)
        {
            ValidationText.Text = LocalizationService.T("Select WireGuard or AmneziaWG.", "Выберите WireGuard или AmneziaWG.");
            return;
        }

        ClientName = name;
        ContainerName = protocol.ContainerName;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
