using System;
using System.Threading.Tasks;
using AmneziaDashboard.App.Services;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class BackupViewModel : ViewModelBase
{
    private readonly DashboardViewModel _dashboard;
    private readonly IServerBackupService _backupService;
    private readonly AppEventLogService _eventLog;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = LocalizationService.T("Ready", "Готово");

    [ObservableProperty]
    private string _lastBackupPath = string.Empty;

    public bool IsConnected => _dashboard.CurrentConnection is not null;
    public string CurrentServer => _dashboard.CurrentConnection is null
        ? LocalizationService.T("No server selected", "Сервер не выбран")
        : $"{_dashboard.CurrentConnection.Name} · {_dashboard.CurrentConnection.Host}";

    public BackupViewModel(
        DashboardViewModel dashboard,
        IServerBackupService backupService,
        AppEventLogService eventLog)
    {
        _dashboard = dashboard;
        _backupService = backupService;
        _eventLog = eventLog;
        _dashboard.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(CurrentServer));
        };
        LocalizationService.LanguageChanged += (_, _) =>
        {
            if (!IsBusy)
                StatusText = LocalizationService.T("Ready", "Готово");
            OnPropertyChanged(nameof(CurrentServer));
        };
    }

    public async Task<ServerBackupResult> CreateBackupAsync(string localPath)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return ServerBackupResult.Fail(LocalizationService.T("Connect to a server first.", "Сначала подключитесь к серверу."));

        return await RunAsync(
            () => _backupService.CreateFullBackupAsync(connection, localPath, CreateProgress()),
            LocalizationService.T("Full server backup created.", "Полная резервная копия сервера создана."));
    }

    public async Task<ServerBackupResult> RestoreBackupAsync(string localPath)
    {
        var connection = _dashboard.CurrentConnection;
        if (connection is null)
            return ServerBackupResult.Fail(LocalizationService.T("Connect to a target server first.", "Сначала подключитесь к целевому серверу."));

        return await RunAsync(
            () => _backupService.RestoreFullBackupAsync(connection, localPath, CreateProgress()),
            LocalizationService.T("Server restore/migration completed.", "Восстановление/перенос сервера завершён."));
    }

    private IProgress<BackupProgress> CreateProgress() => new Progress<BackupProgress>(p =>
    {
        ProgressPercent = p.Percent;
        StatusText = LocalizeStage(p.Stage);
    });

    private static string LocalizeStage(string stage)
    {
        if (!LocalizationService.IsRussian)
            return stage;

        var map = new (string En, string Ru)[]
        {
            ("Connecting to source server", "Подключение к исходному серверу"),
            ("Reading Docker metadata", "Чтение метаданных Docker"),
            ("Archiving /opt/amnezia", "Архивация /opt/amnezia"),
            ("Archiving Docker volumes", "Архивация Docker-томов"),
            ("Archived volume", "Архивирован том"),
            ("Saving Docker images", "Сохранение Docker-образов"),
            ("Preparing portable restore metadata", "Подготовка данных для переноса"),
            ("Packing backup", "Упаковка резервной копии"),
            ("Downloading backup to this computer", "Скачивание резервной копии на компьютер"),
            ("Cleaning temporary server files", "Удаление временных файлов на сервере"),
            ("Backup completed", "Резервная копия создана"),
            ("Connecting to target server", "Подключение к целевому серверу"),
            ("Uploading backup", "Загрузка резервной копии"),
            ("Validating backup", "Проверка резервной копии"),
            ("Creating safety backup on target server", "Создание страховочной копии на целевом сервере"),
            ("Stopping existing Amnezia containers", "Остановка существующих контейнеров Amnezia"),
            ("Loading Docker images", "Загрузка Docker-образов"),
            ("Restoring /opt/amnezia", "Восстановление /opt/amnezia"),
            ("Restoring Docker volumes", "Восстановление Docker-томов"),
            ("Restored volume", "Восстановлен том"),
            ("Restoring Docker networks", "Восстановление Docker-сетей"),
            ("Recreating Amnezia containers", "Пересоздание контейнеров Amnezia"),
            ("Final verification", "Финальная проверка"),
            ("Restore completed", "Восстановление завершено")
        };

        foreach (var item in map)
            if (stage.StartsWith(item.En, StringComparison.OrdinalIgnoreCase))
                return item.Ru + stage[item.En.Length..];

        return stage;
    }

    private async Task<ServerBackupResult> RunAsync(Func<Task<ServerBackupResult>> action, string successText)
    {
        if (IsBusy)
            return ServerBackupResult.Fail(LocalizationService.T("Another backup operation is already running.", "Уже выполняется другая операция резервного копирования."));

        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            var result = await action();
            if (result.Success)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Message) ? successText : LocalizationService.TranslateExternalMessage(result.Message);
                LastBackupPath = result.BackupPath;
                _eventLog.Success("Backup", StatusText);
            }
            else
            {
                StatusText = LocalizationService.T($"Error: {result.ErrorMessage}", $"Ошибка: {result.ErrorMessage}");
                _eventLog.Error("Backup", StatusText);
            }
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
