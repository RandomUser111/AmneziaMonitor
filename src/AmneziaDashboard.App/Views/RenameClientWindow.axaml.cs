using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmneziaDashboard.App.Views;

public partial class RenameClientWindow : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameClientWindow()
    {
        InitializeComponent();
    }

    public RenameClientWindow(string currentName)
        : this()
    {
        NameTextBox.Text = currentName;
        NameTextBox.SelectAll();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NameTextBox.Focus();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var value = NameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorTextBlock.Text = "Введите имя клиента.";
            return;
        }

        NewName = value;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
