using System;
using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public class SshServerProbeService : IServerProbeService
{
    public async Task<ServerProbeResult> ProbeAsync(
        ServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new ServerProbeResult();

            try
            {
                var authenticationMethod =
                    new PasswordAuthenticationMethod(
                        connection.Username,
                        connection.Password);

                var connectionInfo = new ConnectionInfo(
                    connection.Host,
                    connection.Port,
                    connection.Username,
                    authenticationMethod)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                using var client = new SshClient(connectionInfo);

                client.Connect();

                if (!client.IsConnected)
                {
                    result.ErrorMessage =
                        "Не удалось установить SSH-соединение.";

                    return result;
                }

                cancellationToken.ThrowIfCancellationRequested();

                result.Hostname =
                    Execute(client, "hostname");

                result.OperatingSystem =
                    Execute(
                        client,
                        "cat /etc/os-release 2>/dev/null | grep '^PRETTY_NAME=' | cut -d= -f2- | tr -d '\"'");

                result.Kernel =
                    Execute(client, "uname -r");

                result.Uptime =
                    Execute(
                        client,
                        @"awk '{s=int($1); d=int(s/86400); h=int((s%86400)/3600); m=int((s%3600)/60); if (d>0) printf ""%dd %dh"",d,h; else if(h>0) printf ""%dh %dm"",h,m; else printf ""%dm"",m}' /proc/uptime");

                result.CpuUsage =
                    Execute(
                        client,
                        @"LC_ALL=C top -bn1 | awk '/Cpu\(s\)/ {printf ""%.0f%%"", 100-$8; exit}'");

                result.MemoryUsage =
                    Execute(
                        client,
                        @"free | awk '/^Mem:/ {printf ""%.0f%%"", ($3/$2)*100}'");

                result.DiskUsage =
                    Execute(
                        client,
                        @"df -P / | awk 'NR==2 {print $5}'");

                var dockerVersion = ExecuteDocker(client, "--version");

                result.DockerInstalled =
                    !string.IsNullOrWhiteSpace(dockerVersion) &&
                    dockerVersion.Contains("Docker version", StringComparison.OrdinalIgnoreCase);

                result.DockerVersion = dockerVersion;

                if (result.DockerInstalled)
                {
                    result.AmneziaContainers =
                        ReadAmneziaContainers(client, cancellationToken);
                }

                result.Success = true;

                client.Disconnect();

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = GetFriendlyError(ex);

                return result;
            }
        }, cancellationToken);
    }

    private static List<AmneziaContainerInfo> ReadAmneziaContainers(
        SshClient client,
        CancellationToken cancellationToken)
    {
        const string format =
            "'{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}'";

        var output =
            ExecuteDocker(client, $"ps -a --format {format}");

        var containers = new List<AmneziaContainerInfo>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = rawLine.Trim();
            var parts = line.Split('|');

            if (parts.Length < 3)
                continue;

            var containerName = parts[0].Trim();
            var image = parts[1].Trim();

            if (!containerName.StartsWith("amnezia-", StringComparison.OrdinalIgnoreCase) &&
                !image.Contains("amnezia", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var status = parts[2].Trim();
            var ports = parts.Length >= 4 ? parts[3].Trim() : string.Empty;

            var protocolKey = DetectProtocolKey(containerName, image);

            if (protocolKey is null)
                continue;

            var isRunning =
                status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Up", StringComparison.OrdinalIgnoreCase);

            var state = isRunning ? "running" : "stopped";

            var item = new AmneziaContainerInfo
            {
                ContainerName = containerName,
                DisplayName = GetDisplayName(protocolKey, containerName),
                ProtocolKey = protocolKey,
                Image = image,
                State = state,
                Status = status,
                Ports = ports,
                IsRunning = isRunning
            };

            if (isRunning)
            {
                item.ClientCount =
                    TryReadClientCount(client, item);
            }

            containers.Add(item);
        }

        return containers
            .OrderByDescending(x => x.IsRunning)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int? TryReadClientCount(
        SshClient client,
        AmneziaContainerInfo container)
    {
        if (!IsSafeDockerName(container.ContainerName))
            return null;

        string? executable = container.ProtocolKey switch
        {
            "awg2" => "awg",
            "awg" => "wg",
            "wireguard" => "wg",
            _ => null
        };

        if (executable is null)
            return null;

        var output = ExecuteDocker(
            client,
            $"exec -i {container.ContainerName} {executable} show all");

        if (string.IsNullOrWhiteSpace(output))
            return null;

        var count = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimStart().StartsWith("peer:", StringComparison.OrdinalIgnoreCase));

        return count;
    }

    private static bool IsSafeDockerName(string value)
    {
        return Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$");
    }

    private static string? DetectProtocolKey(
        string containerName,
        string image)
    {
        var value = $"{containerName} {image}".ToLowerInvariant();

        if (value.Contains("awg2"))
            return "awg2";

        if (value.Contains("openvpn-cloak"))
            return "cloak";

        if (value.Contains("ssxray"))
            return "ssxray";

        if (value.Contains("wireguard"))
            return "wireguard";

        if (value.Contains("openvpn"))
            return "openvpn";

        if (value.Contains("shadowsocks"))
            return "shadowsocks";

        if (value.Contains("ipsec"))
            return "ipsec";

        if (value.Contains("xray"))
            return "xray";

        if (value.Contains("awg"))
            return "awg";

        return null;
    }

    private static string GetDisplayName(
        string protocolKey,
        string containerName)
    {
        return protocolKey switch
        {
            "awg2" => "AmneziaWG",
            "awg" => "AmneziaWG Legacy",
            "wireguard" => "WireGuard",
            "openvpn" => "OpenVPN",
            "cloak" => "OpenVPN over Cloak",
            "shadowsocks" => "OpenVPN over SS",
            "ipsec" => "IKEv2 / IPsec",
            "xray" => "XRay",
            "ssxray" => "Shadowsocks / XRay",
            _ => containerName
        };
    }

    private static string ExecuteDocker(
        SshClient client,
        string arguments)
    {
        var direct = Execute(client, $"docker {arguments} 2>/dev/null");

        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        return Execute(
            client,
            $"sudo -n docker {arguments} 2>/dev/null");
    }

    private static string Execute(
        SshClient client,
        string commandText)
    {
        using var command = client.CreateCommand(commandText);

        command.CommandTimeout = TimeSpan.FromSeconds(10);

        var output = command.Execute();

        return output.Trim();
    }

    private static string GetFriendlyError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("permission denied") ||
            message.Contains("authentication"))
        {
            return "Ошибка авторизации. Проверьте логин и пароль.";
        }

        if (message.Contains("timed out") ||
            message.Contains("timeout"))
        {
            return "Сервер не ответил за отведённое время.";
        }

        if (message.Contains("actively refused") ||
            message.Contains("connection refused"))
        {
            return "SSH-соединение отклонено сервером. Проверьте адрес и порт.";
        }

        return ex.Message;
    }
}
