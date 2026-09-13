using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AmneziaDashboard.App.Services;

public static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(root, "AmneziaMonitor");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "startup.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O}  {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Startup diagnostics must never prevent the app from starting.
        }
    }

    public static void WriteException(string stage, Exception exception)
    {
        Write($"FATAL [{stage}] {exception}");
    }

    public static void ShowFatalError(Exception exception)
    {
        try
        {
            var message =
                "Amnezia Monitor could not start.\n\n" +
                $"Error: {exception.Message}\n\n" +
                $"Diagnostic log: {LogPath}";

            if (OperatingSystem.IsWindows())
            {
                MessageBoxW(IntPtr.Zero, message, "Amnezia Monitor", 0x00000010);
                return;
            }

            Console.Error.WriteLine(message);
        }
        catch
        {
            // Last-resort error reporting must not hide the original failure.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
