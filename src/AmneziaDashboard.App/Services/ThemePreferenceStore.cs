using System;
using System.IO;
using System.Text.Json;

namespace AmneziaDashboard.App.Services;

public static class ThemePreferenceStore
{
    private const string DefaultTheme = "Light";

    private static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AmneziaMonitor", "settings.json");
        }
    }

    public static string LoadTheme()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return DefaultTheme;

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return Normalize(settings?.Theme);
        }
        catch
        {
            return DefaultTheme;
        }
    }

    public static void SaveTheme(string theme)
    {
        try
        {
            var path = SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var settings = new AppSettings
            {
                Theme = Normalize(theme)
            };

            var tempPath = path + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }
        catch
        {
            // Ошибка сохранения темы не должна мешать работе приложения.
        }
    }

    private static string Normalize(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "dark" => "Dark",
            "system" => "System",
            _ => "Light"
        };
    }

    private sealed class AppSettings
    {
        public string Theme { get; set; } = DefaultTheme;
    }
}
