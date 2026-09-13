using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmneziaDashboard.App.Views;

public partial class BackupRestoreConfirmWindow : Window
{
    public BackupRestoreConfirmWindow() => InitializeComponent();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Restore_Click(object? sender, RoutedEventArgs e) => Close(true);
}
