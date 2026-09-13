using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmneziaDashboard.App.Views;

public partial class DeleteServerWindow : Window
{
    public DeleteServerWindow()
        : this("Сервер", string.Empty)
    {
    }

    public DeleteServerWindow(string serverName, string serverAddress)
    {
        InitializeComponent();
        ServerNameText.Text = serverName;
        ServerAddressText.Text = serverAddress;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
