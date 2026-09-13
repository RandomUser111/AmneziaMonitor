using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;

namespace AmneziaDashboard.App.Services;

public sealed class AppEventLogService
{
    private readonly object _sync = new();
    private readonly string _filePath;

    public ObservableCollection<AppLogEntry> Entries { get; } = [];

    public AppEventLogService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmneziaMonitor");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "events.jsonl");
        LoadRecent();
    }

    public void Info(string category, string message) => Add("Инфо", category, message);

    public void Success(string category, string message) => Add("Успех", category, message);

    public void Warning(string category, string message) => Add("Внимание", category, message);

    public void Error(string category, string message) => Add("Ошибка", category, message);

    public void Clear()
    {
        void Apply()
        {
            Entries.Clear();
            try
            {
                lock (_sync)
                {
                    File.WriteAllText(_filePath, string.Empty);
                }
            }
            catch
            {
                // Журнал не должен мешать работе приложения.
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void Add(string level, string category, string message)
    {
        var dto = new AppLogRecord
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Category = category,
            Message = message
        };

        try
        {
            lock (_sync)
            {
                File.AppendAllText(_filePath, JsonSerializer.Serialize(dto) + Environment.NewLine);
            }
        }
        catch
        {
            // Локальный журнал не должен ломать основную функциональность.
        }

        var entry = AppLogEntry.Create(dto.Timestamp, dto.Level, dto.Category, dto.Message);

        void Apply()
        {
            Entries.Insert(0, entry);
            while (Entries.Count > 1000)
                Entries.RemoveAt(Entries.Count - 1);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void LoadRecent()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var records = File.ReadLines(_filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(500)
                .Select(line =>
                {
                    try
                    {
                        return JsonSerializer.Deserialize<AppLogRecord>(line);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(x => x is not null)
                .Cast<AppLogRecord>()
                .OrderByDescending(x => x.Timestamp)
                .ToList();

            foreach (var record in records)
                Entries.Add(AppLogEntry.Create(record.Timestamp, record.Level, record.Category, record.Message));
        }
        catch
        {
            // Поврежденный журнал можно просто начать заново отображать с текущей сессии.
        }
    }

    private sealed class AppLogRecord
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

public sealed class AppLogEntry
{
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#EF4444"));

    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string TimeText => Timestamp.ToString("dd.MM.yyyy HH:mm:ss");

    public IBrush LevelBrush => Level switch
    {
        "Успех" => SuccessBrush,
        "Внимание" => WarningBrush,
        "Ошибка" => ErrorBrush,
        _ => InfoBrush
    };

    internal static AppLogEntry Create(
        DateTimeOffset timestamp,
        string level,
        string category,
        string message)
    {
        return new AppLogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Category = category,
            Message = message
        };
    }
}
