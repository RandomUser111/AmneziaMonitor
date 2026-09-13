using Avalonia.Controls;
using Avalonia.Interactivity;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class ClientTrafficView : UserControl
{
    public ClientTrafficView()
    {
        InitializeComponent();
    }

    private async void HourButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.SetPeriodAsync(0);
    }

    private async void SixHoursButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.SetPeriodAsync(1);
    }

    private async void DayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.SetPeriodAsync(2);
    }

    private async void WeekButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.SetPeriodAsync(3);
    }

    private async void MonthButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.SetPeriodAsync(4);
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientTrafficViewModel vm)
            await vm.RefreshAsync();
    }
}
