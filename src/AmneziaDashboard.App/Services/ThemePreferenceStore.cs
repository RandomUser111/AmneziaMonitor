namespace AmneziaDashboard.App.Services;

public static class ThemePreferenceStore
{
    public static string LoadTheme() => AppPreferenceStore.LoadTheme();
    public static string LoadLanguage() => AppPreferenceStore.LoadLanguage();
    public static void SaveTheme(string theme) => AppPreferenceStore.SaveTheme(theme);
    public static void SaveLanguage(string language) => AppPreferenceStore.SaveLanguage(language);
}
