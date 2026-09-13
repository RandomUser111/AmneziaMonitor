using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public sealed class SshServerBackupService : IServerBackupService
{
    private const string BackupFormat = "amnezia-monitor-full-v1";

    public Task<ServerBackupResult> CreateFullBackupAsync(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CreateBackup(connection, localPath, progress, cancellationToken), cancellationToken);
    }

    public Task<ServerBackupResult> RestoreFullBackupAsync(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => RestoreBackup(connection, localPath, progress, cancellationToken), cancellationToken);
    }

    private static ServerBackupResult CreateBackup(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return ServerBackupResult.Fail("Backup path is empty.");

        var tempId = Guid.NewGuid().ToString("N");
        var remoteDir = $"/tmp/amnezia-monitor-backup-{tempId}";
        var remoteBundle = $"/tmp/amnezia-monitor-{tempId}.ambackup";

        try
        {
            Report(progress, 2, "Connecting to source server…");
            using var ssh = CreateSshClient(connection);
            using var sftp = CreateSftpClient(connection);
            ssh.Connect();
            sftp.Connect();

            cancellationToken.ThrowIfCancellationRequested();
            var docker = DetectDockerPrefix(ssh);
            if (docker is null)
                return ServerBackupResult.Fail("Docker is not available on the source server.");
            var root = DetectPrivilegePrefix(ssh);
            if (root is null)
                return ServerBackupResult.Fail("Root access or passwordless sudo is required for full backup.");

            ExecuteChecked(ssh, $"mkdir -p {ShellQuote(remoteDir)}/volumes", TimeSpan.FromSeconds(20));

            var containerNames = SplitLines(Execute(ssh,
                $"{docker} ps -a --filter name=amnezia- --format '{{{{.Names}}}}'",
                TimeSpan.FromSeconds(20)))
                .Where(IsSafeDockerName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (containerNames.Count == 0)
                return ServerBackupResult.Fail("No Amnezia Docker containers were found on the source server.");

            Report(progress, 8, "Reading Docker metadata…");
            var joinedNames = string.Join(" ", containerNames.Select(ShellQuote));
            var inspectJson = ExecuteChecked(ssh, $"{docker} inspect {joinedNames}", TimeSpan.FromMinutes(2));
            var parsed = ParseContainers(inspectJson);

            var networkNames = parsed
                .SelectMany(x => x.Networks.Select(n => n.Name))
                .Where(x => x is not "bridge" and not "host" and not "none")
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var networkInspect = networkNames.Count == 0
                ? "[]"
                : ExecuteChecked(ssh,
                    $"{docker} network inspect {string.Join(" ", networkNames.Select(ShellQuote))}",
                    TimeSpan.FromMinutes(1));

            var volumeNames = parsed
                .SelectMany(x => x.Volumes)
                .Select(x => x.Name)
                .Where(IsSafeDockerName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var images = parsed
                .Select(x => x.Image)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var manifest = new BackupManifest
            {
                Format = BackupFormat,
                CreatedUtc = DateTimeOffset.UtcNow,
                SourceName = connection.Name,
                SourceHost = connection.Host,
                Containers = parsed.Select(x => new BackupContainer
                {
                    Name = x.Name,
                    Image = x.Image,
                    WasRunning = x.WasRunning
                }).ToList(),
                Volumes = volumeNames,
                Networks = networkNames
            };

            Report(progress, 15, "Archiving /opt/amnezia…");
            var optArchive = $"{remoteDir}/opt-amnezia.tar.gz";
            var optExists = Execute(ssh, "test -d /opt/amnezia && echo yes || echo no", TimeSpan.FromSeconds(10)).Trim() == "yes";
            if (optExists)
            {
                ExecuteChecked(ssh,
                    $"{root}tar -C / -czf {ShellQuote(optArchive)} opt/amnezia",
                    TimeSpan.FromMinutes(10));
            }
            else
            {
                ExecuteChecked(ssh, $"tar -czf {ShellQuote(optArchive)} --files-from /dev/null", TimeSpan.FromSeconds(20));
            }

            Report(progress, 28, "Archiving Docker volumes…");
            var volumeIndex = 0;
            foreach (var volume in volumeNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                volumeIndex++;
                var mountPoint = ExecuteChecked(ssh,
                    $"{docker} volume inspect -f '{{{{.Mountpoint}}}}' {ShellQuote(volume)}",
                    TimeSpan.FromSeconds(20)).Trim();
                if (string.IsNullOrWhiteSpace(mountPoint))
                    continue;

                var target = $"{remoteDir}/volumes/{volume}.tar.gz";
                ExecuteChecked(ssh,
                    $"{root}tar -C {ShellQuote(mountPoint)} -czf {ShellQuote(target)} .",
                    TimeSpan.FromMinutes(15));
                Report(progress, 28 + (int)(17d * volumeIndex / Math.Max(1, volumeNames.Count)), $"Archived volume {volume}");
            }

            Report(progress, 48, "Saving Docker images…");
            if (images.Count > 0)
            {
                ExecuteChecked(ssh,
                    $"{docker} save -o {ShellQuote(remoteDir + "/images.tar")} {string.Join(" ", images.Select(ShellQuote))}",
                    TimeSpan.FromHours(1));
            }
            else
            {
                ExecuteChecked(ssh, $"touch {ShellQuote(remoteDir + "/images.tar")}", TimeSpan.FromSeconds(10));
            }

            Report(progress, 65, "Preparing portable restore metadata…");
            UploadText(sftp, remoteDir + "/manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
            UploadText(sftp, remoteDir + "/containers.json", inspectJson);
            UploadText(sftp, remoteDir + "/networks.json", networkInspect);
            UploadText(sftp, remoteDir + "/recreate.sh", BuildRecreateScript(parsed));
            UploadText(sftp, remoteDir + "/networks.sh", BuildNetworkScript(networkInspect));

            var systemInfo = Execute(ssh,
                $"printf 'hostname='; hostname; printf 'kernel='; uname -r; {docker} version --format 'docker={{{{.Server.Version}}}}' 2>/dev/null || true",
                TimeSpan.FromSeconds(30));
            UploadText(sftp, remoteDir + "/system.txt", systemInfo);

            Report(progress, 72, "Packing backup…");
            ExecuteChecked(ssh,
                $"tar -C {ShellQuote(remoteDir)} -czf {ShellQuote(remoteBundle)} .",
                TimeSpan.FromHours(1));

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 80, "Downloading backup to this computer…");
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempLocal = localPath + ".part";
            using (var output = File.Create(tempLocal))
                sftp.DownloadFile(remoteBundle, output);

            File.Move(tempLocal, localPath, true);
            var sha256 = ComputeSha256(localPath);
            File.WriteAllText(localPath + ".sha256", $"{sha256}  {Path.GetFileName(localPath)}{Environment.NewLine}");
            Report(progress, 98, "Cleaning temporary server files…");
            Execute(ssh, $"rm -rf {ShellQuote(remoteDir)} {ShellQuote(remoteBundle)}", TimeSpan.FromMinutes(2));

            sftp.Disconnect();
            ssh.Disconnect();
            Report(progress, 100, "Backup completed.");
            return ServerBackupResult.Ok(localPath, "Full Amnezia backup created successfully.");
        }
        catch (OperationCanceledException)
        {
            TryCleanupRemote(connection, remoteDir, remoteBundle);
            return ServerBackupResult.Fail("Backup operation was cancelled.");
        }
        catch (Exception ex)
        {
            TryCleanupRemote(connection, remoteDir, remoteBundle);
            return ServerBackupResult.Fail(GetFriendlyError(ex));
        }
    }

    private static ServerBackupResult RestoreBackup(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(localPath))
            return ServerBackupResult.Fail("Backup file does not exist.");

        var checksumPath = localPath + ".sha256";
        if (File.Exists(checksumPath))
        {
            var expected = File.ReadAllText(checksumPath).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(expected, ComputeSha256(localPath), StringComparison.OrdinalIgnoreCase))
                return ServerBackupResult.Fail("Backup checksum verification failed. The backup file may be corrupted or incomplete.");
        }

        var tempId = Guid.NewGuid().ToString("N");
        var remoteBundle = $"/tmp/amnezia-monitor-restore-{tempId}.ambackup";
        var remoteDir = $"/tmp/amnezia-monitor-restore-{tempId}";
        var safetyArchive = $"/tmp/amnezia-monitor-pre-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}.tar.gz";
        var oldSuffix = $"pre-restore-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var destructivePhaseStarted = false;

        try
        {
            Report(progress, 2, "Connecting to target server…");
            using var ssh = CreateSshClient(connection);
            using var sftp = CreateSftpClient(connection);
            ssh.Connect();
            sftp.Connect();

            var docker = DetectDockerPrefix(ssh);
            if (docker is null)
                return ServerBackupResult.Fail("Docker is not available on the target server.");
            var root = DetectPrivilegePrefix(ssh);
            if (root is null)
                return ServerBackupResult.Fail("Root access or passwordless sudo is required for full restore.");

            ExecuteChecked(ssh, $"mkdir -p {ShellQuote(remoteDir)}", TimeSpan.FromSeconds(20));
            Report(progress, 8, "Uploading backup…");
            using (var input = File.OpenRead(localPath))
                sftp.UploadFile(input, remoteBundle, true);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 28, "Validating backup…");
            ExecuteChecked(ssh,
                $"tar -C {ShellQuote(remoteDir)} -xzf {ShellQuote(remoteBundle)}",
                TimeSpan.FromHours(1));

            var manifestJson = DownloadText(sftp, remoteDir + "/manifest.json");
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, JsonOptions);
            if (manifest is null || manifest.Format != BackupFormat || manifest.Containers.Count == 0)
                return ServerBackupResult.Fail("The selected file is not a supported full Amnezia Monitor backup.");

            Report(progress, 35, "Creating safety backup on target server…");
            ExecuteChecked(ssh,
                $"if test -d /opt/amnezia; then {root}tar -C / -czf {ShellQuote(safetyArchive)} opt/amnezia; else tar -czf {ShellQuote(safetyArchive)} --files-from /dev/null; fi",
                TimeSpan.FromMinutes(10));

            var existing = SplitLines(Execute(ssh,
                $"{docker} ps -a --filter name=amnezia- --format '{{{{.Names}}}}'",
                TimeSpan.FromSeconds(20)))
                .Where(IsSafeDockerName)
                .ToList();

            Report(progress, 42, "Stopping existing Amnezia containers…");
            destructivePhaseStarted = true;
            foreach (var name in existing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Execute(ssh, $"{docker} stop {ShellQuote(name)} >/dev/null 2>&1 || true", TimeSpan.FromMinutes(2));
                ExecuteChecked(ssh, $"{docker} rename {ShellQuote(name)} {ShellQuote(name + "-" + oldSuffix)}", TimeSpan.FromSeconds(30));
            }

            Report(progress, 50, "Loading Docker images…");
            var imagesPath = remoteDir + "/images.tar";
            var imageSize = Execute(ssh, $"stat -c %s {ShellQuote(imagesPath)} 2>/dev/null || echo 0", TimeSpan.FromSeconds(10)).Trim();
            if (long.TryParse(imageSize, out var bytes) && bytes > 0)
                ExecuteChecked(ssh, $"{docker} load -i {ShellQuote(imagesPath)}", TimeSpan.FromHours(1));

            Report(progress, 62, "Restoring /opt/amnezia…");
            ExecuteChecked(ssh, $"{root}rm -rf /opt/amnezia", TimeSpan.FromMinutes(2));
            ExecuteChecked(ssh,
                $"{root}tar -C / -xzf {ShellQuote(remoteDir + "/opt-amnezia.tar.gz")}",
                TimeSpan.FromMinutes(15));

            Report(progress, 70, "Restoring Docker volumes…");
            var volumeIndex = 0;
            foreach (var volume in manifest.Volumes.Where(IsSafeDockerName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                volumeIndex++;
                ExecuteChecked(ssh, $"{docker} volume create {ShellQuote(volume)} >/dev/null", TimeSpan.FromSeconds(30));
                var mountPoint = ExecuteChecked(ssh,
                    $"{docker} volume inspect -f '{{{{.Mountpoint}}}}' {ShellQuote(volume)}",
                    TimeSpan.FromSeconds(20)).Trim();
                var archive = $"{remoteDir}/volumes/{volume}.tar.gz";
                ExecuteChecked(ssh, $"test -f {ShellQuote(archive)} && {root}find {ShellQuote(mountPoint)} -mindepth 1 -maxdepth 1 -exec rm -rf -- {{}} + && {root}tar -C {ShellQuote(mountPoint)} -xzf {ShellQuote(archive)}", TimeSpan.FromMinutes(15));
                Report(progress, 70 + (int)(10d * volumeIndex / Math.Max(1, manifest.Volumes.Count)), $"Restored volume {volume}");
            }

            Report(progress, 82, "Restoring Docker networks…");
            ExecuteChecked(ssh, $"chmod +x {ShellQuote(remoteDir + "/networks.sh")} {ShellQuote(remoteDir + "/recreate.sh")}", TimeSpan.FromSeconds(20));
            ExecuteChecked(ssh, $"DOCKER={ShellQuote(docker)} sh {ShellQuote(remoteDir + "/networks.sh")}", TimeSpan.FromMinutes(5));

            Report(progress, 88, "Recreating Amnezia containers…");
            ExecuteChecked(ssh, $"DOCKER={ShellQuote(docker)} sh {ShellQuote(remoteDir + "/recreate.sh")}", TimeSpan.FromMinutes(15));

            foreach (var expected in manifest.Containers)
            {
                var exists = Execute(ssh,
                    $"{docker} inspect {ShellQuote(expected.Name)} >/dev/null 2>&1 && echo yes || echo no",
                    TimeSpan.FromSeconds(15)).Trim();
                if (exists != "yes")
                    throw new InvalidOperationException($"Container {expected.Name} was not recreated.");

                if (!expected.WasRunning)
                    Execute(ssh, $"{docker} stop {ShellQuote(expected.Name)} >/dev/null 2>&1 || true", TimeSpan.FromMinutes(2));
            }

            Report(progress, 96, "Final verification…");
            foreach (var oldName in existing.Select(x => x + "-" + oldSuffix))
                Execute(ssh, $"{docker} rm -f {ShellQuote(oldName)} >/dev/null 2>&1 || true", TimeSpan.FromMinutes(2));

            Execute(ssh, $"rm -rf {ShellQuote(remoteDir)} {ShellQuote(remoteBundle)}", TimeSpan.FromMinutes(2));
            sftp.Disconnect();
            ssh.Disconnect();
            Report(progress, 100, "Restore completed.");

            return new ServerBackupResult
            {
                Success = true,
                BackupPath = localPath,
                SafetyBackupPath = safetyArchive,
                Message = $"Restore completed. Target-side safety copy of the previous /opt/amnezia: {safetyArchive}"
            };
        }
        catch (OperationCanceledException)
        {
            TryCleanupRemote(connection, remoteDir, remoteBundle);
            return ServerBackupResult.Fail("Restore operation was cancelled.");
        }
        catch (Exception ex)
        {
            // Best-effort rollback only after the target containers were renamed.
            if (!destructivePhaseStarted)
            {
                TryCleanupRemote(connection, remoteDir, remoteBundle);
                return ServerBackupResult.Fail(GetFriendlyError(ex));
            }

            try
            {
                using var rollback = CreateSshClient(connection);
                rollback.Connect();
                var docker = DetectDockerPrefix(rollback);
                var root = DetectPrivilegePrefix(rollback) ?? string.Empty;
                if (docker is not null)
                {
                    foreach (var line in SplitLines(Execute(rollback,
                        $"{docker} ps -a --filter name=amnezia- --format '{{{{.Names}}}}'",
                        TimeSpan.FromSeconds(20))))
                    {
                        if (line.EndsWith("-" + oldSuffix, StringComparison.Ordinal) || !IsSafeDockerName(line))
                            continue;
                        Execute(rollback, $"{docker} rm -f {ShellQuote(line)} >/dev/null 2>&1 || true", TimeSpan.FromMinutes(2));
                    }

                    foreach (var oldName in SplitLines(Execute(rollback,
                        $"{docker} ps -a --filter name={ShellQuote(oldSuffix)} --format '{{{{.Names}}}}'",
                        TimeSpan.FromSeconds(20))))
                    {
                        if (!oldName.EndsWith("-" + oldSuffix, StringComparison.Ordinal) || !IsSafeDockerName(oldName))
                            continue;
                        var original = oldName[..^(oldSuffix.Length + 1)];
                        Execute(rollback, $"{docker} rename {ShellQuote(oldName)} {ShellQuote(original)} && {docker} start {ShellQuote(original)} >/dev/null 2>&1 || true", TimeSpan.FromMinutes(2));
                    }
                }

                var safetyExists = Execute(rollback, $"test -f {ShellQuote(safetyArchive)} && echo yes || echo no", TimeSpan.FromSeconds(10)).Trim();
                if (safetyExists == "yes")
                {
                    Execute(rollback, $"{root}rm -rf /opt/amnezia", TimeSpan.FromMinutes(2));
                    Execute(rollback, $"{root}tar -C / -xzf {ShellQuote(safetyArchive)}", TimeSpan.FromMinutes(10));
                }
            }
            catch
            {
                // Preserve the original restore error.
            }

            TryCleanupRemote(connection, remoteDir, remoteBundle);
            return ServerBackupResult.Fail($"Restore failed. A best-effort rollback was attempted. {GetFriendlyError(ex)}");
        }
    }

    private static string BuildRecreateScript(IReadOnlyList<ContainerDefinition> containers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/sh");
        sb.AppendLine("set -eu");
        sb.AppendLine("DOCKER=${DOCKER:-docker}");

        foreach (var c in containers)
        {
            var args = new List<string> { "$DOCKER run -d", "--name " + ShellQuote(c.Name) };
            if (!string.IsNullOrWhiteSpace(c.Hostname)) args.Add("--hostname " + ShellQuote(c.Hostname));
            if (c.Privileged) args.Add("--privileged");
            if (!string.IsNullOrWhiteSpace(c.RestartPolicy) && c.RestartPolicy != "no") args.Add("--restart " + ShellQuote(c.RestartPolicy));
            if (!string.IsNullOrWhiteSpace(c.LogDriver)) args.Add("--log-driver " + ShellQuote(c.LogDriver));
            foreach (var option in c.LogOptions) args.Add("--log-opt " + ShellQuote(option.Key + "=" + option.Value));
            foreach (var ulimit in c.Ulimits) args.Add("--ulimit " + ShellQuote(ulimit));
            foreach (var cap in c.CapAdd) args.Add("--cap-add " + ShellQuote(cap));
            foreach (var sysctl in c.Sysctls) args.Add("--sysctl " + ShellQuote(sysctl.Key + "=" + sysctl.Value));
            foreach (var env in c.Environment) args.Add("-e " + ShellQuote(env));
            foreach (var bind in c.Binds) args.Add("-v " + ShellQuote(bind));
            foreach (var volume in c.Volumes.Where(v => !c.Binds.Any(b => b.Contains(":" + v.Destination, StringComparison.Ordinal))))
                args.Add("-v " + ShellQuote($"{volume.Name}:{volume.Destination}{(volume.ReadOnly ? ":ro" : string.Empty)}"));
            foreach (var port in c.Ports) args.Add("-p " + ShellQuote(port));
            foreach (var device in c.Devices) args.Add("--device " + ShellQuote(device));
            foreach (var dns in c.Dns) args.Add("--dns " + ShellQuote(dns));
            foreach (var extra in c.ExtraHosts) args.Add("--add-host " + ShellQuote(extra));
            if (!string.IsNullOrWhiteSpace(c.User)) args.Add("--user " + ShellQuote(c.User));
            if (!string.IsNullOrWhiteSpace(c.WorkingDir)) args.Add("--workdir " + ShellQuote(c.WorkingDir));

            var primaryNetwork = c.Networks.FirstOrDefault(n => n.Name is not "bridge" and not "host" and not "none");
            if (primaryNetwork is not null)
            {
                args.Add("--network " + ShellQuote(primaryNetwork.Name));
                if (!string.IsNullOrWhiteSpace(primaryNetwork.IpAddress)) args.Add("--ip " + ShellQuote(primaryNetwork.IpAddress));
                if (!string.IsNullOrWhiteSpace(primaryNetwork.GlobalIPv6Address)) args.Add("--ip6 " + ShellQuote(primaryNetwork.GlobalIPv6Address));
            }
            else if (!string.IsNullOrWhiteSpace(c.NetworkMode) && (c.NetworkMode is "host" or "none"))
            {
                args.Add("--network " + ShellQuote(c.NetworkMode));
            }

            if (!string.IsNullOrWhiteSpace(c.Entrypoint)) args.Add("--entrypoint " + ShellQuote(c.Entrypoint));
            args.Add(ShellQuote(c.Image));
            foreach (var cmd in c.Command) args.Add(ShellQuote(cmd));

            sb.AppendLine(string.Join(" \\\n  ", args));

            foreach (var extraNetwork in c.Networks.Where(n => primaryNetwork is not null && n.Name != primaryNetwork.Name && n.Name is not "bridge" and not "host" and not "none"))
            {
                var ip = string.IsNullOrWhiteSpace(extraNetwork.IpAddress) ? string.Empty : " --ip " + ShellQuote(extraNetwork.IpAddress);
                var ip6 = string.IsNullOrWhiteSpace(extraNetwork.GlobalIPv6Address) ? string.Empty : " --ip6 " + ShellQuote(extraNetwork.GlobalIPv6Address);
                sb.AppendLine($"$DOCKER network connect{ip}{ip6} {ShellQuote(extraNetwork.Name)} {ShellQuote(c.Name)} || true");
            }
        }

        return sb.ToString();
    }

    private static string BuildNetworkScript(string networkInspectJson)
    {
        var sb = new StringBuilder("#!/bin/sh\nset -eu\nDOCKER=${DOCKER:-docker}\n");
        try
        {
            using var doc = JsonDocument.Parse(networkInspectJson);
            foreach (var network in doc.RootElement.EnumerateArray())
            {
                var name = GetString(network, "Name");
                if (string.IsNullOrWhiteSpace(name) || name is "bridge" or "host" or "none")
                    continue;
                var driver = GetString(network, "Driver");
                var args = new List<string> { "$DOCKER network create" };
                if (!string.IsNullOrWhiteSpace(driver)) args.Add("--driver " + ShellQuote(driver));

                if (network.TryGetProperty("IPAM", out var ipam) && ipam.TryGetProperty("Config", out var configs) && configs.ValueKind == JsonValueKind.Array)
                {
                    var cfg = configs.EnumerateArray().FirstOrDefault();
                    if (cfg.ValueKind == JsonValueKind.Object)
                    {
                        var subnet = GetString(cfg, "Subnet");
                        var gateway = GetString(cfg, "Gateway");
                        if (!string.IsNullOrWhiteSpace(subnet)) args.Add("--subnet " + ShellQuote(subnet));
                        if (!string.IsNullOrWhiteSpace(gateway)) args.Add("--gateway " + ShellQuote(gateway));
                    }
                }

                args.Add(ShellQuote(name));
                sb.AppendLine($"$DOCKER network inspect {ShellQuote(name)} >/dev/null 2>&1 || {string.Join(" ", args)} >/dev/null");
            }
        }
        catch
        {
            // Empty script is still valid for backups without custom networks.
        }
        return sb.ToString();
    }

    private static List<ContainerDefinition> ParseContainers(string inspectJson)
    {
        using var doc = JsonDocument.Parse(inspectJson);
        var list = new List<ContainerDefinition>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = GetString(item, "Name").TrimStart('/');
            if (!IsSafeDockerName(name))
                continue;

            var config = item.GetProperty("Config");
            var host = item.GetProperty("HostConfig");
            var def = new ContainerDefinition
            {
                Name = name,
                Image = GetString(config, "Image"),
                Hostname = GetString(config, "Hostname"),
                User = GetString(config, "User"),
                WorkingDir = GetString(config, "WorkingDir"),
                Privileged = host.TryGetProperty("Privileged", out var priv) && priv.ValueKind == JsonValueKind.True,
                NetworkMode = GetString(host, "NetworkMode"),
                WasRunning = item.TryGetProperty("State", out var state) && state.TryGetProperty("Running", out var running) && running.ValueKind == JsonValueKind.True
            };

            if (host.TryGetProperty("RestartPolicy", out var restart)) def.RestartPolicy = GetString(restart, "Name");
            if (host.TryGetProperty("LogConfig", out var logConfig) && logConfig.ValueKind == JsonValueKind.Object)
            {
                def.LogDriver = GetString(logConfig, "Type");
                if (logConfig.TryGetProperty("Config", out var logOptions) && logOptions.ValueKind == JsonValueKind.Object)
                    foreach (var prop in logOptions.EnumerateObject()) def.LogOptions[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            if (host.TryGetProperty("Ulimits", out var ulimits) && ulimits.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in ulimits.EnumerateArray())
                {
                    var nameValue = GetString(u, "Name");
                    if (string.IsNullOrWhiteSpace(nameValue)) continue;
                    var soft = u.TryGetProperty("Soft", out var softValue) ? softValue.GetInt64() : 0;
                    var hard = u.TryGetProperty("Hard", out var hardValue) ? hardValue.GetInt64() : soft;
                    def.Ulimits.Add($"{nameValue}={soft}:{hard}");
                }
            }
            if (config.TryGetProperty("Entrypoint", out var ep) && ep.ValueKind == JsonValueKind.Array) def.Entrypoint = ep.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            if (config.TryGetProperty("Cmd", out var cmd) && cmd.ValueKind == JsonValueKind.Array) def.Command.AddRange(cmd.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (config.TryGetProperty("Env", out var env) && env.ValueKind == JsonValueKind.Array) def.Environment.AddRange(env.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (host.TryGetProperty("Binds", out var binds) && binds.ValueKind == JsonValueKind.Array) def.Binds.AddRange(binds.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (host.TryGetProperty("CapAdd", out var caps) && caps.ValueKind == JsonValueKind.Array) def.CapAdd.AddRange(caps.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (host.TryGetProperty("Dns", out var dns) && dns.ValueKind == JsonValueKind.Array) def.Dns.AddRange(dns.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (host.TryGetProperty("ExtraHosts", out var extras) && extras.ValueKind == JsonValueKind.Array) def.ExtraHosts.AddRange(extras.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
            if (host.TryGetProperty("Sysctls", out var sysctls) && sysctls.ValueKind == JsonValueKind.Object)
                foreach (var prop in sysctls.EnumerateObject()) def.Sysctls[prop.Name] = prop.Value.GetString() ?? string.Empty;
            if (host.TryGetProperty("Devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
                foreach (var d in devices.EnumerateArray())
                {
                    var hp = GetString(d, "PathOnHost"); var cp = GetString(d, "PathInContainer"); var perm = GetString(d, "CgroupPermissions");
                    if (!string.IsNullOrWhiteSpace(hp) && !string.IsNullOrWhiteSpace(cp)) def.Devices.Add($"{hp}:{cp}:{perm}");
                }

            if (host.TryGetProperty("PortBindings", out var bindings) && bindings.ValueKind == JsonValueKind.Object)
            {
                foreach (var portProp in bindings.EnumerateObject())
                {
                    if (portProp.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var binding in portProp.Value.EnumerateArray())
                    {
                        var hostPort = GetString(binding, "HostPort");
                        var hostIp = GetString(binding, "HostIp");
                        if (string.IsNullOrWhiteSpace(hostPort)) continue;
                        def.Ports.Add(string.IsNullOrWhiteSpace(hostIp) || hostIp == "0.0.0.0"
                            ? $"{hostPort}:{portProp.Name}"
                            : $"{hostIp}:{hostPort}:{portProp.Name}");
                    }
                }
            }

            if (item.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var mount in mounts.EnumerateArray())
                {
                    if (GetString(mount, "Type") != "volume") continue;
                    var volume = new VolumeDefinition
                    {
                        Name = GetString(mount, "Name"),
                        Destination = GetString(mount, "Destination"),
                        ReadOnly = mount.TryGetProperty("RW", out var rw) && rw.ValueKind == JsonValueKind.False
                    };
                    if (!string.IsNullOrWhiteSpace(volume.Name)) def.Volumes.Add(volume);
                }
            }

            if (item.TryGetProperty("NetworkSettings", out var networkSettings) && networkSettings.TryGetProperty("Networks", out var networks) && networks.ValueKind == JsonValueKind.Object)
            {
                foreach (var n in networks.EnumerateObject())
                    def.Networks.Add(new NetworkAttachment
                    {
                        Name = n.Name,
                        IpAddress = GetString(n.Value, "IPAddress"),
                        GlobalIPv6Address = GetString(n.Value, "GlobalIPv6Address")
                    });
            }

            list.Add(def);
        }
        return list;
    }

    private static SshClient CreateSshClient(ServerConnection connection) => new(CreateConnectionInfo(connection));
    private static SftpClient CreateSftpClient(ServerConnection connection) => new(CreateConnectionInfo(connection));

    private static ConnectionInfo CreateConnectionInfo(ServerConnection connection)
    {
        var auth = new PasswordAuthenticationMethod(connection.Username, connection.Password);
        return new ConnectionInfo(connection.Host, connection.Port, connection.Username, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static void TryCleanupRemote(ServerConnection connection, params string[] paths)
    {
        try
        {
            using var client = CreateSshClient(connection);
            client.Connect();
            var safePaths = paths.Where(x => !string.IsNullOrWhiteSpace(x) && x.StartsWith("/tmp/amnezia-monitor-", StringComparison.Ordinal))
                .Select(ShellQuote)
                .ToArray();
            if (safePaths.Length > 0)
                Execute(client, "rm -rf " + string.Join(" ", safePaths), TimeSpan.FromMinutes(2));
        }
        catch
        {
            // Cleanup is best effort only.
        }
    }

    private static string? DetectPrivilegePrefix(SshClient client)
    {
        var uid = Execute(client, "id -u", TimeSpan.FromSeconds(10)).Trim();
        if (uid == "0") return string.Empty;
        var sudo = Execute(client, "sudo -n true >/dev/null 2>&1 && echo yes || true", TimeSpan.FromSeconds(10)).Trim();
        return sudo == "yes" ? "sudo -n " : null;
    }

    private static string? DetectDockerPrefix(SshClient client)
    {
        var direct = Execute(client, "docker info >/dev/null 2>&1 && echo docker || true", TimeSpan.FromSeconds(20)).Trim();
        if (direct == "docker") return "docker";
        var sudo = Execute(client, "sudo -n docker info >/dev/null 2>&1 && echo 'sudo -n docker' || true", TimeSpan.FromSeconds(20)).Trim();
        return sudo == "sudo -n docker" ? sudo : null;
    }

    private static string ExecuteChecked(SshClient client, string commandText, TimeSpan timeout)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        var output = command.Execute() ?? string.Empty;
        if (command.ExitStatus != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.Error) ? output.Trim() : command.Error.Trim());
        return output.Trim();
    }

    private static string Execute(SshClient client, string commandText, TimeSpan timeout)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        return (command.Execute() ?? string.Empty).Trim();
    }

    private static void UploadText(SftpClient sftp, string path, string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        sftp.UploadFile(stream, path, true);
    }

    private static string DownloadText(SftpClient sftp, string path)
    {
        using var stream = new MemoryStream();
        sftp.DownloadFile(path, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IEnumerable<string> SplitLines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static bool IsSafeDockerName(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$");
    private static string ShellQuote(string value) => "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
    private static string GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static void Report(IProgress<BackupProgress>? progress, int percent, string stage) => progress?.Report(new BackupProgress { Percent = Math.Clamp(percent, 0, 100), Stage = stage });

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string GetFriendlyError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)) return "Permission denied. Root access or passwordless sudo is required for full backup/restore.";
        if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "The server did not respond before the operation timed out.";
        return msg;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private sealed class BackupManifest
    {
        public string Format { get; set; } = BackupFormat;
        public DateTimeOffset CreatedUtc { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string SourceHost { get; set; } = string.Empty;
        public List<BackupContainer> Containers { get; set; } = [];
        public List<string> Volumes { get; set; } = [];
        public List<string> Networks { get; set; } = [];
    }

    private sealed class BackupContainer
    {
        public string Name { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public bool WasRunning { get; set; }
    }

    private sealed class ContainerDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public string RestartPolicy { get; set; } = string.Empty;
        public string NetworkMode { get; set; } = string.Empty;
        public string Entrypoint { get; set; } = string.Empty;
        public string LogDriver { get; set; } = string.Empty;
        public bool Privileged { get; set; }
        public bool WasRunning { get; set; }
        public List<string> Environment { get; } = [];
        public List<string> Command { get; } = [];
        public List<string> Binds { get; } = [];
        public List<string> CapAdd { get; } = [];
        public Dictionary<string, string> Sysctls { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> LogOptions { get; } = new(StringComparer.Ordinal);
        public List<string> Ulimits { get; } = [];
        public List<string> Ports { get; } = [];
        public List<string> Devices { get; } = [];
        public List<string> Dns { get; } = [];
        public List<string> ExtraHosts { get; } = [];
        public List<VolumeDefinition> Volumes { get; } = [];
        public List<NetworkAttachment> Networks { get; } = [];
    }

    private sealed class VolumeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public bool ReadOnly { get; set; }
    }

    private sealed class NetworkAttachment
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string GlobalIPv6Address { get; set; } = string.Empty;
    }
}
