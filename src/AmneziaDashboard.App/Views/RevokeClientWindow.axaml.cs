using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmneziaDashboard.App.Views;

public partial class RevokeClientWindow : Window
{
    public RevokeClientWindow()
        : this(string.Empty, string.Empty, string.Empty)
    {
    }

    public RevokeClientWindow(string clientName, string protocol, string address)
    {
        InitializeComponent();

        ClientNameText.Text = clientName;
        ProtocolText.Text = protocol;
        AddressText.Text = address;
    }

    private void RevokeButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
