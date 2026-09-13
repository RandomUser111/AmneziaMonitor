using Avalonia.Controls;
using Avalonia.Interactivity;

using AmneziaDashboard.App.Services;
namespace AmneziaDashboard.App.Views;

public partial class RestoreClientConfigWindow : Window
{
    public RestoreClientConfigWindow()
        : this(LocalizationService.T("Client", "Клиент"), "—", "—")
    {
    }

    public RestoreClientConfigWindow(string clientName, string protocol, string address)
    {
        InitializeComponent();
        ClientNameText.Text = clientName;
        ProtocolText.Text = protocol;
        AddressText.Text = address;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void RestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
