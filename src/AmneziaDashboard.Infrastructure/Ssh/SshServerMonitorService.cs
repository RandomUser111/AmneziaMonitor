using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public class SshServerMonitorService : IServerMonitorService
{
    public async Task<ServerMonitorSnapshot> ReadAsync(
        ServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var snapshot = new ServerMonitorSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow
            };

            try
            {
                using var client = CreateClient(connection);
                client.Connect();

                if (!client.IsConnected)
                {
                    snapshot.ErrorMessage = "Не удалось установить SSH-соединение.";
                    return snapshot;
                }

                cancellationToken.ThrowIfCancellationRequested();

                snapshot.CpuUsage = ValueOrDash(Execute(
                    client,
                    @"LC_ALL=C top -bn1 | awk '/Cpu\(s\)/ {printf ""%.0f%%"", 100-$8; exit}'"));

                snapshot.MemoryUsage = ValueOrDash(Execute(
                    client,
                    @"free | awk '/^Mem:/ {printf ""%.0f%%"", ($3/$2)*100}'"));

                snapshot.DiskUsage = ValueOrDash(Execute(
                    client,
                    @"df -P / | awk 'NR==2 {print $5}'"));

                snapshot.Uptime = ValueOrDash(Execute(
                    client,
                    @"awk '{s=int($1); d=int(s/86400); h=int((s%86400)/3600); m=int((s%3600)/60); if (d>0) printf ""%dd %dh"",d,h; else if(h>0) printf ""%dh %dm"",h,m; else printf ""%dm"",m}' /proc/uptime"));

                ReadNetworkCounters(client, snapshot);

                snapshot.AmneziaContainers = ReadAmneziaContainers(client, cancellationToken);

                foreach (var container in snapshot.AmneziaContainers.Where(x => x.IsRunning))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var peers = ReadWireGuardPeers(client, container);
                    if (peers.Count == 0)
                        continue;

                    snapshot.Peers.AddRange(peers);
                    container.ClientCount = peers.Count;
                }

                snapshot.Success = true;
                client.Disconnect();

                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Success = false;
                snapshot.ErrorMessage = GetFriendlyError(ex);
                return snapshot;
            }
        }, cancellationToken);
    }

    private static SshClient CreateClient(ServerConnection connection)
    {
        var authenticationMethod = new PasswordAuthenticationMethod(
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

        return new SshClient(connectionInfo);
    }

    private static void ReadNetworkCounters(
        SshClient client,
        ServerMonitorSnapshot snapshot)
    {
        var interfaceName = Execute(
            client,
            "ip route show default 2>/dev/null | awk 'NR==1 {print $5}'");

        if (string.IsNullOrWhiteSpace(interfaceName) || !IsSafeInterfaceName(interfaceName))
            return;

        snapshot.NetworkInterface = interfaceName;

        var rx = Execute(
            client,
            $"cat /sys/class/net/{interfaceName}/statistics/rx_bytes 2>/dev/null");

        var tx = Execute(
            client,
            $"cat /sys/class/net/{interfaceName}/statistics/tx_bytes 2>/dev/null");

        long.TryParse(rx, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receivedBytes);
        long.TryParse(tx, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sentBytes);

        snapshot.NetworkReceivedBytes = receivedBytes;
        snapshot.NetworkSentBytes = sentBytes;
    }

    private static List<AmneziaContainerInfo> ReadAmneziaContainers(
        SshClient client,
        CancellationToken cancellationToken)
    {
        const string format = "'{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}'";
        var output = ExecuteDocker(client, $"ps -a --format {format}");
        var containers = new List<AmneziaContainerInfo>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parts = rawLine.Trim().Split('|');
            if (parts.Length < 3)
                continue;

            var containerName = parts[0].Trim();
            var image = parts[1].Trim();

            if (!containerName.StartsWith("amnezia-", StringComparison.OrdinalIgnoreCase) &&
                !image.Contains("amnezia", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var protocolKey = DetectProtocolKey(containerName, image);
            if (protocolKey is null)
                continue;

            var status = parts[2].Trim();
            var isRunning = status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Up", StringComparison.OrdinalIgnoreCase);

            containers.Add(new AmneziaContainerInfo
            {
                ContainerName = containerName,
                DisplayName = GetDisplayName(protocolKey, containerName),
                ProtocolKey = protocolKey,
                Image = image,
                State = isRunning ? "running" : "stopped",
                Status = status,
                Ports = parts.Length >= 4 ? parts[3].Trim() : string.Empty,
                IsRunning = isRunning
            });
        }

        return containers
            .OrderByDescending(x => x.IsRunning)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<VpnPeerInfo> ReadWireGuardPeers(
        SshClient client,
        AmneziaContainerInfo container)
    {
        if (!IsSafeDockerName(container.ContainerName))
            return [];

        var executable = container.ProtocolKey switch
        {
            "awg2" => "awg",
            "awg" => "wg",
            "wireguard" => "wg",
            _ => null
        };

        if (executable is null)
            return [];

        var interfacesOutput = ExecuteDocker(
            client,
            $"exec -i {container.ContainerName} {executable} show interfaces");

        var interfaceName = interfacesOutput
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(interfaceName) || !IsSafeInterfaceName(interfaceName))
            return [];

        var dump = ExecuteDocker(
            client,
            $"exec -i {container.ContainerName} {executable} show {interfaceName} dump");

        if (string.IsNullOrWhiteSpace(dump))
            return [];

        var names = ReadClientNames(client, container);
        var peers = new List<VpnPeerInfo>();

        var lines = dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Первая строка dump описывает сам интерфейс. Остальные строки — peers.
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Trim().Split('\t');
            if (fields.Length < 8)
                continue;

            var publicKey = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(publicKey))
                continue;

            long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var latestHandshake);
            long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var receivedBytes);
            long.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sentBytes);

            peers.Add(new VpnPeerInfo
            {
                ContainerName = container.ContainerName,
                ProtocolName = container.DisplayName,
                ClientId = publicKey,
                ClientName = names.TryGetValue(publicKey, out var clientName)
                    ? clientName
                    : ShortKey(publicKey),
                Endpoint = fields[2].Trim(),
                AllowedIps = fields[3].Trim(),
                LatestHandshakeUnix = latestHandshake,
                ReceivedBytes = receivedBytes,
                SentBytes = sentBytes
            });
        }

        return peers;
    }

    private static Dictionary<string, string> ReadClientNames(
        SshClient client,
        AmneziaContainerInfo container)
    {
        var path = container.ProtocolKey switch
        {
            "awg2" => "/opt/amnezia/awg/clientsTable",
            "awg" => "/opt/amnezia/awg/clientsTable",
            "wireguard" => "/opt/amnezia/wireguard/clientsTable",
            _ => null
        };

        if (path is null || !IsSafeDockerName(container.ContainerName))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var json = ExecuteDocker(
            client,
            $"exec -i {container.ContainerName} cat {path}");

        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(json);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("clientId", out var idElement))
                        continue;

                    var clientId = idElement.GetString();
                    if (string.IsNullOrWhiteSpace(clientId))
                        continue;

                    var clientName = ReadClientName(item);
                    if (!string.IsNullOrWhiteSpace(clientName))
                        result[clientId] = clientName;
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Поддержка старого формата clientsTable: ключ объекта = clientId.
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var clientName = ReadClientName(property.Value);
                    if (!string.IsNullOrWhiteSpace(clientName))
                        result[property.Name] = clientName;
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string ReadClientName(JsonElement item)
    {
        if (item.TryGetProperty("userData", out var userData) &&
            userData.ValueKind == JsonValueKind.Object &&
            userData.TryGetProperty("clientName", out var nameElement))
        {
            return nameElement.GetString() ?? string.Empty;
        }

        if (item.TryGetProperty("clientName", out var directName))
            return directName.GetString() ?? string.Empty;

        return string.Empty;
    }

    private static string ShortKey(string key)
    {
        if (key.Length <= 14)
            return key;

        return $"{key[..7]}…{key[^6..]}";
    }

    private static string? DetectProtocolKey(string containerName, string image)
    {
        var value = $"{containerName} {image}".ToLowerInvariant();

        if (value.Contains("awg2")) return "awg2";
        if (value.Contains("openvpn-cloak")) return "cloak";
        if (value.Contains("ssxray")) return "ssxray";
        if (value.Contains("wireguard")) return "wireguard";
        if (value.Contains("openvpn")) return "openvpn";
        if (value.Contains("shadowsocks")) return "shadowsocks";
        if (value.Contains("ipsec")) return "ipsec";
        if (value.Contains("xray")) return "xray";
        if (value.Contains("awg")) return "awg";

        return null;
    }

    private static string GetDisplayName(string protocolKey, string containerName)
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

    private static string ExecuteDocker(SshClient client, string arguments)
    {
        var direct = Execute(client, $"docker {arguments} 2>/dev/null");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        return Execute(client, $"sudo -n docker {arguments} 2>/dev/null");
    }

    private static string Execute(SshClient client, string commandText)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromSeconds(10);
        return command.Execute().Trim();
    }

    private static bool IsSafeDockerName(string value)
    {
        return Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$");
    }

    private static bool IsSafeInterfaceName(string value)
    {
        return Regex.IsMatch(value, "^[A-Za-z0-9_.:@-]+$");
    }

    private static string ValueOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string GetFriendlyError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("permission denied") || message.Contains("authentication"))
            return "Ошибка авторизации. Проверьте логин и пароль.";

        if (message.Contains("timed out") || message.Contains("timeout"))
            return "Сервер не ответил за отведённое время.";

        if (message.Contains("actively refused") || message.Contains("connection refused"))
            return "SSH-соединение отклонено сервером. Проверьте адрес и порт.";

        return ex.Message;
    }
}
