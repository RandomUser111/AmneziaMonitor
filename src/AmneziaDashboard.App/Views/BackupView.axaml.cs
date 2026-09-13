using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.App.ViewModels;

namespace AmneziaDashboard.App.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
    }

    private async void CreateBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.T("Save Amnezia backup", "Сохранить резервную копию Amnezia"),
            SuggestedFileName = $"AmneziaMonitor-backup-{DateTime.Now:yyyyMMdd-HHmm}.ambackup",
            DefaultExtension = "ambackup",
            FileTypeChoices =
            [
                new FilePickerFileType("Amnezia Monitor backup") { Patterns = ["*.ambackup"] }
            ]
        });

        if (file is null)
            return;

        await vm.CreateBackupAsync(file.Path.LocalPath);
    }

    private async void RestoreBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("Select Amnezia backup", "Выберите резервную копию Amnezia"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Amnezia Monitor backup") { Patterns = ["*.ambackup"] }
            ]
        });

        if (files.Count == 0)
            return;

        var owner = top as Window;
        if (owner is not null)
        {
            var confirm = new BackupRestoreConfirmWindow();
            var accepted = await confirm.ShowDialog<bool>(owner);
            if (!accepted)
                return;
        }

        await vm.RestoreBackupAsync(files[0].Path.LocalPath);
    }
}
