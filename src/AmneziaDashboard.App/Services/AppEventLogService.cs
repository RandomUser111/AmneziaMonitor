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

    public void Info(string category, string message) => Add("Info", category, message);

    public void Success(string category, string message) => Add("Success", category, message);

    public void Warning(string category, string message) => Add("Warning", category, message);

    public void Error(string category, string message) => Add("Error", category, message);

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
                // Logging must never interfere with the application.
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
            Category = NormalizeCategory(category),
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
            // Local logging must not break primary functionality.
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
            {
                Entries.Add(AppLogEntry.Create(
                    record.Timestamp,
                    NormalizeLevel(record.Level),
                    NormalizeCategory(record.Category),
                    record.Message));
            }
        }
        catch
        {
            // A damaged local log can be ignored; the current session can start a new one.
        }
    }

    public static string NormalizeCategory(string? category)
    {
        return category?.Trim() switch
        {
            "Мониторинг" => "Monitoring",
            "Серверы" => "Servers",
            "Клиенты" => "Clients",
            "Протоколы" => "Protocols",
            "Docker" => "Docker",
            "SSH" => "SSH",
            "Monitoring" => "Monitoring",
            "Servers" => "Servers",
            "Clients" => "Clients",
            "Protocols" => "Protocols",
            _ => category?.Trim() ?? string.Empty
        };
    }

    private static string NormalizeLevel(string? level)
    {
        return level?.Trim() switch
        {
            "Инфо" => "Info",
            "Успех" => "Success",
            "Внимание" => "Warning",
            "Ошибка" => "Error",
            "Info" => "Info",
            "Success" => "Success",
            "Warning" => "Warning",
            "Error" => "Error",
            _ => "Info"
        };
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

    public string TimeText => Timestamp.ToString("g");

    public string LevelText => Level switch
    {
        "Success" => LocalizationService.T("Success", "Успех"),
        "Warning" => LocalizationService.T("Warning", "Внимание"),
        "Error" => LocalizationService.T("Error", "Ошибка"),
        _ => LocalizationService.T("Info", "Инфо")
    };

    public string CategoryText => Category switch
    {
        "Monitoring" => LocalizationService.T("Monitoring", "Мониторинг"),
        "Servers" => LocalizationService.T("Servers", "Серверы"),
        "Clients" => LocalizationService.T("Clients", "Клиенты"),
        "Protocols" => LocalizationService.T("Protocols", "Протоколы"),
        _ => Category
    };

    public IBrush LevelBrush => Level switch
    {
        "Success" => SuccessBrush,
        "Warning" => WarningBrush,
        "Error" => ErrorBrush,
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
