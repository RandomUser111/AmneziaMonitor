using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
    }

    private void ClearEvents_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogsViewModel vm)
            vm.ClearEvents();
    }

    private async void RefreshDockerLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogsViewModel vm)
            await vm.RefreshDockerLogsAsync();
    }
}
