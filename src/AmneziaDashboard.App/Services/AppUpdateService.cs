using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AmneziaDashboard.App.Services;

public sealed class AppUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/RandomUser111/AmneziaMonitor/releases/latest";
    private readonly DesktopNotificationService _notifications;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler? StateChanged;

    public bool IsChecking { get; private set; }
    public bool IsUpdateAvailable { get; private set; }
    public string LatestVersion { get; private set; } = string.Empty;
    public string ReleaseUrl { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public string CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }

    public AppUpdateService(DesktopNotificationService notifications)
    {
        _notifications = notifications;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"AmneziaMonitor/{CurrentVersion}");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && !AppPreferenceStore.LoadAutomaticUpdateChecks())
            return;

        if (!force && AppPreferenceStore.LoadLastUpdateCheckUtc() is { } last &&
            DateTimeOffset.UtcNow - last < TimeSpan.FromHours(12))
        {
            return;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            IsChecking = true;
            ErrorMessage = string.Empty;
            RaiseChanged();

            using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() : null;

            LatestVersion = NormalizeVersionText(tag);
            ReleaseUrl = url ?? string.Empty;
            IsUpdateAvailable = IsNewerThanCurrent(LatestVersion);
            LastCheckedAt = DateTimeOffset.UtcNow;
            AppPreferenceStore.SaveLastUpdateCheckUtc(LastCheckedAt.Value);

            if (IsUpdateAvailable && AppPreferenceStore.LoadAutomaticUpdateChecks())
            {
                await _notifications.NotifyAsync(
                    LocalizationService.T("Amnezia Monitor update", "Обновление Amnezia Monitor"),
                    LocalizationService.T(
                        $"Version {LatestVersion} is available. You are using {CurrentVersion}.",
                        $"Доступна версия {LatestVersion}. Сейчас установлена {CurrentVersion}."));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an update failure.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            LastCheckedAt = DateTimeOffset.UtcNow;
            AppPreferenceStore.SaveLastUpdateCheckUtc(LastCheckedAt.Value);
        }
        finally
        {
            IsChecking = false;
            RaiseChanged();
            _gate.Release();
        }
    }

    public bool OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleaseUrl))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseUrl,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetStatusText()
    {
        if (IsChecking)
            return LocalizationService.T("Checking for updates…", "Проверка обновлений…");

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
            return LocalizationService.T("Update check failed.", "Не удалось проверить обновления.");

        if (IsUpdateAvailable)
            return LocalizationService.T(
                $"Version {LatestVersion} is available (current: {CurrentVersion}).",
                $"Доступна версия {LatestVersion} (текущая: {CurrentVersion}).");

        if (LastCheckedAt.HasValue)
            return LocalizationService.T(
                $"You are up to date. Current version: {CurrentVersion}.",
                $"Установлена актуальная версия: {CurrentVersion}.");

        return LocalizationService.T(
            $"Current version: {CurrentVersion}.",
            $"Текущая версия: {CurrentVersion}.");
    }

    private bool IsNewerThanCurrent(string candidate)
    {
        if (!Version.TryParse(candidate, out var remote))
            return false;

        if (!Version.TryParse(CurrentVersion, out var current))
            return false;

        return remote > current;
    }

    private static string NormalizeVersionText(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return string.Empty;

        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        var suffixIndex = value.IndexOf('-');
        if (suffixIndex >= 0)
            value = value[..suffixIndex];

        return value;
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
