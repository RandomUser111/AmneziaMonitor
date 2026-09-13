using System;
using System.IO;
using System.Text.Json;

namespace AmneziaDashboard.App.Services;

public static class AppPreferenceStore
{
    private const string DefaultTheme = "Light";
    private const string DefaultLanguage = LocalizationService.English;

    private static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AmneziaMonitor", "settings.json");
        }
    }

    public static string LoadTheme() => LoadSettings().Theme;
    public static string LoadLanguage() => LoadSettings().Language;
    public static bool LoadNotificationsEnabled() => LoadSettings().NotificationsEnabled;
    public static bool LoadCloseToTray() => LoadSettings().CloseToTray;
    public static bool LoadMinimizeToTray() => LoadSettings().MinimizeToTray;
    public static bool LoadAutomaticUpdateChecks() => LoadSettings().AutomaticUpdateChecks;
    public static DateTimeOffset? LoadLastUpdateCheckUtc() => LoadSettings().LastUpdateCheckUtc;

    public static void SaveTheme(string theme) => Update(x => x.Theme = NormalizeTheme(theme));
    public static void SaveLanguage(string language) => Update(x => x.Language = NormalizeLanguage(language));
    public static void SaveNotificationsEnabled(bool value) => Update(x => x.NotificationsEnabled = value);
    public static void SaveCloseToTray(bool value) => Update(x => x.CloseToTray = value);
    public static void SaveMinimizeToTray(bool value) => Update(x => x.MinimizeToTray = value);
    public static void SaveAutomaticUpdateChecks(bool value) => Update(x => x.AutomaticUpdateChecks = value);
    public static void SaveLastUpdateCheckUtc(DateTimeOffset value) => Update(x => x.LastUpdateCheckUtc = value);

    private static void Update(Action<AppSettings> update)
    {
        var settings = LoadSettings();
        update(settings);
        SaveSettings(settings);
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return CreateDefaults();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaults();
            settings.Theme = NormalizeTheme(settings.Theme);
            settings.Language = NormalizeLanguage(settings.Language);
            return settings;
        }
        catch
        {
            return CreateDefaults();
        }
    }

    private static void SaveSettings(AppSettings settings)
    {
        try
        {
            var path = SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            settings.Theme = NormalizeTheme(settings.Theme);
            settings.Language = NormalizeLanguage(settings.Language);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }
        catch
        {
            // Settings persistence must never prevent startup.
        }
    }

    private static AppSettings CreateDefaults() => new()
    {
        Theme = DefaultTheme,
        Language = DefaultLanguage,
        NotificationsEnabled = true,
        CloseToTray = true,
        MinimizeToTray = true,
        AutomaticUpdateChecks = true
    };

    private static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "dark" => "Dark",
        "system" => "System",
        _ => DefaultTheme
    };

    private static string NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "russian" or "ru" or "ru-ru" => LocalizationService.Russian,
        _ => DefaultLanguage
    };

    private sealed class AppSettings
    {
        public string Theme { get; set; } = DefaultTheme;
        public string Language { get; set; } = DefaultLanguage;
        public bool NotificationsEnabled { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool AutomaticUpdateChecks { get; set; } = true;
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    }
}
