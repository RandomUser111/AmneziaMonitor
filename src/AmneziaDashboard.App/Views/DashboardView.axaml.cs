using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void ConnectServer_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow ||
            mainWindow.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.ShowServersPage();
    }
}
