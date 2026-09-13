using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Infrastructure.Security;

public sealed class CrossPlatformSecretStore : ISecretStore
{
    private const string TargetPrefix = "AmneziaMonitor/SSH/";
    private readonly string? _secretToolPath;

    public CrossPlatformSecretStore()
    {
        if (OperatingSystem.IsLinux())
            _secretToolPath = FindExecutable("secret-tool");
    }

    public bool IsAvailable =>
        OperatingSystem.IsWindows() ||
        (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(_secretToolPath));

    public string BackendName =>
        OperatingSystem.IsWindows()
            ? "Windows Credential Manager"
            : OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(_secretToolPath)
                ? "Linux Secret Service"
                : "Защищённое хранилище недоступно";

    public async Task<string?> GetPasswordAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        if (OperatingSystem.IsWindows())
            return ReadWindowsCredential(TargetPrefix + profileId);

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(_secretToolPath))
            return await ReadLinuxSecretAsync(profileId, cancellationToken);

        return null;
    }

    public async Task<OperationResult> SetPasswordAsync(
        string profileId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        if (string.IsNullOrEmpty(password))
            return OperationResult.Fail("Пустой пароль не сохраняется.");

        if (OperatingSystem.IsWindows())
        {
            return WriteWindowsCredential(TargetPrefix + profileId, password)
                ? OperationResult.Ok("Пароль сохранён в Windows Credential Manager.")
                : OperationResult.Fail(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(_secretToolPath))
            return await WriteLinuxSecretAsync(profileId, password, cancellationToken);

        return OperationResult.Fail(
            "Защищённое хранилище паролей недоступно. В Linux установите пакет libsecret/secret-tool.");
    }

    public async Task<OperationResult> DeletePasswordAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        if (OperatingSystem.IsWindows())
        {
            if (CredDelete(TargetPrefix + profileId, CredentialType.Generic, 0))
                return OperationResult.Ok();

            var error = Marshal.GetLastWin32Error();
            return error == 1168
                ? OperationResult.Ok()
                : OperationResult.Fail(new Win32Exception(error).Message);
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(_secretToolPath))
        {
            var result = await RunSecretToolAsync(
                ["clear", "amnezia-monitor", "profile-id", profileId],
                null,
                cancellationToken);

            // secret-tool returns a non-zero code when no matching secret exists.
            return result.ExitCode is 0 or 1
                ? OperationResult.Ok()
                : OperationResult.Fail(ValueOrFallback(result.Error, "Не удалось удалить пароль из Secret Service."));
        }

        return OperationResult.Ok();
    }

    private async Task<string?> ReadLinuxSecretAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var result = await RunSecretToolAsync(
            ["lookup", "amnezia-monitor", "profile-id", profileId],
            null,
            cancellationToken);

        if (result.ExitCode != 0)
            return null;

        var value = result.Output.TrimEnd('\r', '\n');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private async Task<OperationResult> WriteLinuxSecretAsync(
        string profileId,
        string password,
        CancellationToken cancellationToken)
    {
        var result = await RunSecretToolAsync(
            [
                "store",
                "--label=Amnezia Monitor SSH",
                "amnezia-monitor",
                "profile-id",
                profileId
            ],
            password,
            cancellationToken);

        return result.ExitCode == 0
            ? OperationResult.Ok("Пароль сохранён в Linux Secret Service.")
            : OperationResult.Fail(ValueOrFallback(
                result.Error,
                "Не удалось сохранить пароль в Linux Secret Service."));
    }

    private async Task<ProcessResult> RunSecretToolAsync(
        IReadOnlyList<string> arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_secretToolPath))
            return new ProcessResult(-1, string.Empty, "secret-tool не найден.");

        var startInfo = new ProcessStartInfo
        {
            FileName = _secretToolPath,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            await process.StandardInput.WriteLineAsync();
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string? FindExecutable(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore invalid PATH entries.
            }
        }

        return null;
    }

    private static void ValidateProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) ||
            profileId.Length > 128 ||
            profileId.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
        {
            throw new ArgumentException("Некорректный идентификатор профиля.", nameof(profileId));
        }
    }

    private static string ValueOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    #region Windows Credential Manager

    private static bool WriteWindowsCredential(string target, string password)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocCoTaskMem(passwordBytes.Length);

        try
        {
            Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);

            var credential = new Credential
            {
                Type = CredentialType.Generic,
                TargetName = target,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistence.LocalMachine,
                UserName = "Amnezia Monitor"
            };

            return CredWrite(ref credential, 0);
        }
        finally
        {
            if (passwordBytes.Length > 0)
            {
                for (var i = 0; i < passwordBytes.Length; i++)
                    passwordBytes[i] = 0;
            }

            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static string? ReadWindowsCredential(string target)
    {
        if (!CredRead(target, CredentialType.Generic, 0, out var credentialPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);

            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private enum CredentialType : uint
    {
        Generic = 1
    }

    private enum CredentialPersistence : uint
    {
        Session = 1,
        LocalMachine = 2,
        Enterprise = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public CredentialType Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CredentialPersistence Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential userCredential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        CredentialType type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        CredentialType type,
        uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    #endregion

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
