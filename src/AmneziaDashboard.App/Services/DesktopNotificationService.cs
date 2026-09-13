using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AmneziaDashboard.App.Services;

public enum DesktopNotificationKind
{
    Information,
    Warning,
    Error
}

public sealed class DesktopNotificationService
{
    private readonly AppEventLogService? _eventLog;
    private DateTimeOffset _lastNotificationAt = DateTimeOffset.MinValue;
    private string _lastMessage = string.Empty;

    public DesktopNotificationService(AppEventLogService? eventLog = null)
    {
        _eventLog = eventLog;
    }

    public async Task NotifyAsync(string title, string message, DesktopNotificationKind kind = DesktopNotificationKind.Information)
    {
        if (!AppPreferenceStore.LoadNotificationsEnabled() || string.IsNullOrWhiteSpace(message))
            return;

        // Prevent duplicate bursts from the 5-second monitoring loop.
        if (message == _lastMessage && DateTimeOffset.UtcNow - _lastNotificationAt < TimeSpan.FromSeconds(20))
            return;

        _lastMessage = message;
        _lastNotificationAt = DateTimeOffset.UtcNow;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await NotifyWindowsAsync(title, message, kind);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                await NotifyLinuxAsync(title, message, kind);
        }
        catch (Exception ex)
        {
            _eventLog?.Warning("Notifications", $"Desktop notification failed: {ex.Message}");
        }
    }

    private static async Task NotifyWindowsAsync(string title, string message, DesktopNotificationKind kind)
    {
        var icon = kind switch
        {
            DesktopNotificationKind.Error => "Error",
            DesktopNotificationKind.Warning => "Warning",
            _ => "Info"
        };

        static string Escape(string value) => value.Replace("'", "''").Replace("\r", " ").Replace("\n", " ");

        var script = $"Add-Type -AssemblyName System.Windows.Forms; " +
                     "$n=New-Object System.Windows.Forms.NotifyIcon; " +
                     "$n.Icon=[System.Drawing.SystemIcons]::Application; $n.Visible=$true; " +
                     $"$n.BalloonTipTitle='{Escape(title)}'; $n.BalloonTipText='{Escape(message)}'; " +
                     $"$n.BalloonTipIcon=[System.Windows.Forms.ToolTipIcon]::{icon}; " +
                     "$n.ShowBalloonTip(5000); Start-Sleep -Seconds 6; $n.Dispose();";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is not null)
            await process.WaitForExitAsync();
    }

    private static async Task NotifyLinuxAsync(string title, string message, DesktopNotificationKind kind)
    {
        var urgency = kind switch
        {
            DesktopNotificationKind.Error => "critical",
            DesktopNotificationKind.Warning => "normal",
            _ => "low"
        };

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "notify-send",
            ArgumentList = { "--app-name=Amnezia Monitor", $"--urgency={urgency}", title, message },
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (process is not null)
            await process.WaitForExitAsync();
    }
}
