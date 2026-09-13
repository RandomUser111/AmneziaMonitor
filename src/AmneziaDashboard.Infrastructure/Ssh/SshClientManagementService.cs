using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public class SshClientManagementService : IClientManagementService
{
    public async Task<OperationResult> RenameClientAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RenameClient(
            connection,
            containerName,
            clientId,
            newName,
            cancellationToken), cancellationToken);
    }

    public async Task<OperationResult> RevokeClientAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RevokeClient(
            connection,
            containerName,
            clientId,
            cancellationToken), cancellationToken);
    }

    public async Task<CreateClientResult> CreateClientAsync(
        ServerConnection connection,
        string containerName,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => CreateClient(
            connection,
            containerName,
            clientName,
            cancellationToken), cancellationToken);
    }

    public async Task<CreateClientResult> RestoreClientConfigAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RestoreClientConfig(
            connection,
            containerName,
            clientId,
            clientName,
            cancellationToken), cancellationToken);
    }

    private static CreateClientResult CreateClient(
        ServerConnection connection,
        string containerName,
        string clientName,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return CreateClientResult.Fail("Некорректное имя Docker-контейнера.");

        var normalizedName = clientName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return CreateClientResult.Fail("Имя клиента не может быть пустым.");

        if (normalizedName.Length > 100)
            return CreateClientResult.Fail("Имя клиента слишком длинное (максимум 100 символов).");

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return CreateClientResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var runtime = DetectRuntime(client, containerName);
            if (runtime is null)
            {
                return CreateClientResult.Fail(
                    "Контейнер не является активным WireGuard/AmneziaWG или интерфейс не запущен.");
            }

            var configPath = DetectServerConfigPath(client, containerName, runtime.Value);
            if (configPath is null)
                return CreateClientResult.Fail("Не удалось определить серверный .conf-файл.");

            var originalConfig = ExecuteDocker(client, $"exec -i {containerName} cat {configPath}");
            if (string.IsNullOrWhiteSpace(originalConfig))
                return CreateClientResult.Fail("Не удалось прочитать серверную конфигурацию.");

            var interfaceValues = ReadInterfaceValues(originalConfig);
            if (!interfaceValues.TryGetValue("Address", out var serverAddress) ||
                string.IsNullOrWhiteSpace(serverAddress))
            {
                return CreateClientResult.Fail("В серверной конфигурации не найден Address интерфейса.");
            }

            var clientAddress = FindFreeClientAddress(serverAddress, originalConfig);
            if (clientAddress is null)
                return CreateClientResult.Fail("В VPN-подсети не найден свободный IPv4-адрес.");

            var privateKey = ExecuteDocker(
                client,
                $"exec -i {containerName} {runtime.Value.Executable} genkey");

            if (!IsSafePeerKey(privateKey))
                return CreateClientResult.Fail("Не удалось сгенерировать приватный ключ клиента.");

            var publicKeyResult = ExecuteDockerCommand(
                client,
                $"exec -i {containerName} sh -c \"printf '%s' '{privateKey}' | {runtime.Value.Executable} pubkey\"");

            var publicKey = publicKeyResult.Output.Trim();
            if (!publicKeyResult.Success || !IsSafePeerKey(publicKey))
                return CreateClientResult.Fail("Не удалось получить public key нового клиента.");

            var serverPublicKey = ExecuteDocker(
                client,
                $"exec -i {containerName} {runtime.Value.Executable} show {runtime.Value.InterfaceName} public-key").Trim();

            if (!IsSafePeerKey(serverPublicKey))
                return CreateClientResult.Fail("Не удалось получить public key сервера.");

            var presharedKey = ReadPresharedKey(client, containerName, configPath, originalConfig);
            if (!IsSafePeerKey(presharedKey))
                return CreateClientResult.Fail("Не удалось прочитать PresharedKey сервера.");

            var listenPort = ReadListenPort(interfaceValues);
            if (listenPort is null)
            {
                var portOutput = ExecuteDocker(
                    client,
                    $"exec -i {containerName} {runtime.Value.Executable} show {runtime.Value.InterfaceName} listen-port");

                if (!int.TryParse(portOutput.Trim(), out var runtimePort) || runtimePort is < 1 or > 65535)
                    return CreateClientResult.Fail("Не удалось определить UDP-порт VPN-сервера.");

                listenPort = runtimePort;
            }

            var peerBlock =
                $"[Peer]\n" +
                $"PublicKey = {publicKey}\n" +
                $"PresharedKey = {presharedKey}\n" +
                $"AllowedIPs = {clientAddress}/32\n";

            var updatedConfig = originalConfig.TrimEnd() + "\n\n" + peerBlock + "\n";

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var configBackupPath = $"{configPath}.dashboard-backup-{timestamp}";
            var backupConfig = CopyFileInContainer(client, containerName, configPath, configBackupPath);
            if (!backupConfig.Success)
            {
                return CreateClientResult.Fail(
                    $"Не удалось создать резервную копию конфигурации: {backupConfig.ErrorMessage}");
            }

            var clientsTablePath = DetectClientsTablePath(client, containerName) ??
                                   DefaultClientsTablePath(configPath);
            string? clientsTableBackupPath = null;
            var clientsTableExisted = FileExistsInContainer(client, containerName, clientsTablePath);
            JsonNode clientsRoot;

            if (clientsTableExisted)
            {
                var originalClientsTable = ExecuteDocker(
                    client,
                    $"exec -i {containerName} cat {clientsTablePath}");

                try
                {
                    clientsRoot = string.IsNullOrWhiteSpace(originalClientsTable)
                        ? new JsonArray()
                        : JsonNode.Parse(originalClientsTable) ?? new JsonArray();
                }
                catch (JsonException)
                {
                    return CreateClientResult.Fail(
                        "clientsTable имеет неизвестный формат JSON. Серверная конфигурация не изменена.");
                }

                clientsTableBackupPath = $"{clientsTablePath}.dashboard-backup-{timestamp}";
                var backupTable = CopyFileInContainer(
                    client,
                    containerName,
                    clientsTablePath,
                    clientsTableBackupPath);

                if (!backupTable.Success)
                {
                    return CreateClientResult.Fail(
                        $"Не удалось создать резервную копию clientsTable: {backupTable.ErrorMessage}");
                }
            }
            else
            {
                clientsRoot = new JsonArray();
            }

            if (!AddToClientsTable(clientsRoot, publicKey, normalizedName))
                return CreateClientResult.Fail("Клиент с таким public key уже присутствует в clientsTable.");

            var clientsJson = clientsRoot.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            cancellationToken.ThrowIfCancellationRequested();

            var writeConfig = WriteTextFileToContainer(
                client,
                containerName,
                configPath,
                updatedConfig);

            if (!writeConfig.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                return CreateClientResult.Fail(
                    $"Не удалось обновить серверную конфигурацию. Исходный файл восстановлен. {writeConfig.ErrorMessage}");
            }

            var writeTable = WriteTextFileToContainer(
                client,
                containerName,
                clientsTablePath,
                clientsJson);

            if (!writeTable.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                if (clientsTableExisted && clientsTableBackupPath is not null)
                    RestoreBackup(client, containerName, clientsTableBackupPath, clientsTablePath);
                else
                    RemoveFileInContainer(client, containerName, clientsTablePath);

                return CreateClientResult.Fail(
                    $"Не удалось обновить clientsTable. Конфигурация восстановлена. {writeTable.ErrorMessage}");
            }

            var sync = SyncRuntimeFromConfig(
                client,
                containerName,
                runtime.Value,
                configPath);

            if (!sync.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                if (clientsTableExisted && clientsTableBackupPath is not null)
                    RestoreBackup(client, containerName, clientsTableBackupPath, clientsTablePath);
                else
                    RemoveFileInContainer(client, containerName, clientsTablePath);

                SyncRuntimeFromConfig(client, containerName, runtime.Value, configPath);

                return CreateClientResult.Fail(
                    "Не удалось применить новый peer. Серверные файлы восстановлены из резервных копий. " +
                    ValueOrFallback(sync.Error, "Неизвестная ошибка wg/awg."));
            }

            var clientConfig = BuildClientConfig(
                connection.Host,
                listenPort.Value,
                clientAddress,
                privateKey,
                serverPublicKey,
                presharedKey,
                interfaceValues);

            client.Disconnect();

            return CreateClientResult.Ok(
                publicKey,
                $"{clientAddress}/32",
                clientConfig,
                "Клиент создан. Сохраните конфигурацию: приватный ключ существует только в показанном профиле.");
        }
        catch (OperationCanceledException)
        {
            return CreateClientResult.Fail("Операция отменена.");
        }
        catch (Exception ex)
        {
            return CreateClientResult.Fail(GetFriendlyError(ex));
        }
    }

    private static CreateClientResult RestoreClientConfig(
        ServerConnection connection,
        string containerName,
        string clientId,
        string clientName,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return CreateClientResult.Fail("Некорректное имя Docker-контейнера.");

        if (!IsSafePeerKey(clientId))
            return CreateClientResult.Fail("Некорректный WireGuard/AmneziaWG public key.");

        var normalizedName = string.IsNullOrWhiteSpace(clientName)
            ? "Клиент"
            : clientName.Trim();

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return CreateClientResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var runtime = DetectRuntime(client, containerName);
            if (runtime is null)
            {
                return CreateClientResult.Fail(
                    "Не удалось определить активный WireGuard/AmneziaWG интерфейс.");
            }

            var configPath = FindPeerConfigPath(client, containerName, clientId);
            if (configPath is null)
                return CreateClientResult.Fail("Не найден серверный .conf-файл этого клиента.");

            var originalConfig = ExecuteDocker(client, $"exec -i {containerName} cat {configPath}");
            if (string.IsNullOrWhiteSpace(originalConfig))
                return CreateClientResult.Fail("Не удалось прочитать серверную конфигурацию.");

            if (!TryGetPeerValues(originalConfig, clientId, out var peerValues))
                return CreateClientResult.Fail("Peer клиента не найден в серверной конфигурации.");

            if (!peerValues.TryGetValue("AllowedIPs", out var allowedIps) ||
                !TryGetClientIpv4(allowedIps, out var clientAddress))
            {
                return CreateClientResult.Fail("Не удалось определить VPN IP существующего клиента.");
            }

            var interfaceValues = ReadInterfaceValues(originalConfig);

            var privateKey = ExecuteDocker(
                client,
                $"exec -i {containerName} {runtime.Value.Executable} genkey").Trim();

            if (!IsSafePeerKey(privateKey))
                return CreateClientResult.Fail("Не удалось сгенерировать новый приватный ключ клиента.");

            var publicKeyResult = ExecuteDockerCommand(
                client,
                $"exec -i {containerName} sh -c \"printf '%s' '{privateKey}' | {runtime.Value.Executable} pubkey\"");

            var publicKey = publicKeyResult.Output.Trim();
            if (!publicKeyResult.Success || !IsSafePeerKey(publicKey))
                return CreateClientResult.Fail("Не удалось получить новый public key клиента.");

            var serverPublicKey = ExecuteDocker(
                client,
                $"exec -i {containerName} {runtime.Value.Executable} show {runtime.Value.InterfaceName} public-key").Trim();

            if (!IsSafePeerKey(serverPublicKey))
                return CreateClientResult.Fail("Не удалось получить public key сервера.");

            var presharedKey = peerValues.TryGetValue("PresharedKey", out var peerPsk)
                ? peerPsk.Trim()
                : ReadPresharedKey(client, containerName, configPath, originalConfig);

            if (!IsSafePeerKey(presharedKey))
                return CreateClientResult.Fail("Не удалось прочитать PresharedKey клиента.");

            var listenPort = ReadListenPort(interfaceValues);
            if (listenPort is null)
            {
                var portOutput = ExecuteDocker(
                    client,
                    $"exec -i {containerName} {runtime.Value.Executable} show {runtime.Value.InterfaceName} listen-port");

                if (!int.TryParse(portOutput.Trim(), out var runtimePort) || runtimePort is < 1 or > 65535)
                    return CreateClientResult.Fail("Не удалось определить UDP-порт VPN-сервера.");

                listenPort = runtimePort;
            }

            if (!TryReplacePeerPublicKey(originalConfig, clientId, publicKey, out var updatedConfig))
                return CreateClientResult.Fail("Не удалось подготовить обновлённую серверную конфигурацию.");

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var configBackupPath = $"{configPath}.dashboard-backup-{timestamp}";
            var backupConfig = CopyFileInContainer(client, containerName, configPath, configBackupPath);
            if (!backupConfig.Success)
            {
                return CreateClientResult.Fail(
                    $"Не удалось создать резервную копию конфигурации: {backupConfig.ErrorMessage}");
            }

            var clientsTablePath = DetectClientsTablePath(client, containerName) ??
                                   DefaultClientsTablePath(configPath);
            var clientsTableExisted = FileExistsInContainer(client, containerName, clientsTablePath);
            string? clientsTableBackupPath = null;
            JsonNode clientsRoot;

            if (clientsTableExisted)
            {
                var originalClientsTable = ExecuteDocker(
                    client,
                    $"exec -i {containerName} cat {clientsTablePath}");

                try
                {
                    clientsRoot = string.IsNullOrWhiteSpace(originalClientsTable)
                        ? new JsonArray()
                        : JsonNode.Parse(originalClientsTable) ?? new JsonArray();
                }
                catch (JsonException)
                {
                    return CreateClientResult.Fail(
                        "clientsTable имеет неизвестный формат JSON. Серверная конфигурация не изменена.");
                }

                clientsTableBackupPath = $"{clientsTablePath}.dashboard-backup-{timestamp}";
                var backupTable = CopyFileInContainer(
                    client,
                    containerName,
                    clientsTablePath,
                    clientsTableBackupPath);

                if (!backupTable.Success)
                {
                    return CreateClientResult.Fail(
                        $"Не удалось создать резервную копию clientsTable: {backupTable.ErrorMessage}");
                }
            }
            else
            {
                clientsRoot = new JsonArray();
            }

            if (!ReplaceClientIdInClientsTable(clientsRoot, clientId, publicKey))
            {
                if (!AddToClientsTable(clientsRoot, publicKey, normalizedName))
                    return CreateClientResult.Fail("Не удалось обновить служебную запись клиента.");
            }

            var clientsJson = clientsRoot.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            cancellationToken.ThrowIfCancellationRequested();

            var writeConfig = WriteTextFileToContainer(
                client,
                containerName,
                configPath,
                updatedConfig);

            if (!writeConfig.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                return CreateClientResult.Fail(
                    $"Не удалось заменить ключ клиента. Исходная конфигурация восстановлена. {writeConfig.ErrorMessage}");
            }

            var writeTable = WriteTextFileToContainer(
                client,
                containerName,
                clientsTablePath,
                clientsJson);

            if (!writeTable.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                if (clientsTableExisted && clientsTableBackupPath is not null)
                    RestoreBackup(client, containerName, clientsTableBackupPath, clientsTablePath);
                else
                    RemoveFileInContainer(client, containerName, clientsTablePath);

                return CreateClientResult.Fail(
                    $"Не удалось обновить clientsTable. Изменения отменены. {writeTable.ErrorMessage}");
            }

            var sync = SyncRuntimeFromConfig(
                client,
                containerName,
                runtime.Value,
                configPath);

            if (!sync.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);
                if (clientsTableExisted && clientsTableBackupPath is not null)
                    RestoreBackup(client, containerName, clientsTableBackupPath, clientsTablePath);
                else
                    RemoveFileInContainer(client, containerName, clientsTablePath);

                SyncRuntimeFromConfig(client, containerName, runtime.Value, configPath);

                return CreateClientResult.Fail(
                    "Не удалось применить новый ключ. Сервер восстановлен из резервных копий. " +
                    ValueOrFallback(sync.Error, "Неизвестная ошибка wg/awg."));
            }

            var clientConfig = BuildClientConfig(
                connection.Host,
                listenPort.Value,
                clientAddress,
                privateKey,
                serverPublicKey,
                presharedKey,
                interfaceValues);

            client.Disconnect();

            return CreateClientResult.Ok(
                publicKey,
                $"{clientAddress}/32",
                clientConfig,
                "Конфигурация восстановлена. Имя и VPN IP сохранены, ключевая пара клиента заменена.");
        }
        catch (OperationCanceledException)
        {
            return CreateClientResult.Fail("Операция отменена.");
        }
        catch (Exception ex)
        {
            return CreateClientResult.Fail(GetFriendlyError(ex));
        }
    }

    private static OperationResult RenameClient(
        ServerConnection connection,
        string containerName,
        string clientId,
        string newName,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return OperationResult.Fail("Некорректное имя Docker-контейнера.");

        var normalizedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return OperationResult.Fail("Имя клиента не может быть пустым.");

        if (normalizedName.Length > 100)
            return OperationResult.Fail("Имя клиента слишком длинное (максимум 100 символов).");

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return OperationResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var clientsTablePath = DetectClientsTablePath(client, containerName);
            if (clientsTablePath is null)
                return OperationResult.Fail("Для этого контейнера пока не поддерживается переименование клиентов.");

            var json = ExecuteDocker(client, $"exec -i {containerName} cat {clientsTablePath}");
            if (string.IsNullOrWhiteSpace(json))
                return OperationResult.Fail("Не удалось прочитать clientsTable на сервере.");

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return OperationResult.Fail("Файл clientsTable имеет неизвестный формат JSON.");
            }

            if (root is null)
                return OperationResult.Fail("Файл clientsTable пуст.");

            var changed = RenameInDocument(root, clientId, normalizedName);
            if (!changed)
                return OperationResult.Fail("Клиент не найден в clientsTable.");

            var updatedJson = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(updatedJson));
            var writeResult = WriteFileToContainer(client, containerName, clientsTablePath, base64);
            if (!writeResult.Success)
                return writeResult;

            client.Disconnect();
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Операция отменена.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(GetFriendlyError(ex));
        }
    }

    private static OperationResult RevokeClient(
        ServerConnection connection,
        string containerName,
        string clientId,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return OperationResult.Fail("Некорректное имя Docker-контейнера.");

        if (!IsSafePeerKey(clientId))
            return OperationResult.Fail("Некорректный WireGuard/AmneziaWG public key.");

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return OperationResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var runtime = DetectRuntime(client, containerName);
            if (runtime is null)
            {
                return OperationResult.Fail(
                    "Не удалось определить активный WireGuard/AmneziaWG интерфейс. Удаление не выполнено.");
            }

            var configPath = FindPeerConfigPath(client, containerName, clientId);
            if (configPath is null)
            {
                return OperationResult.Fail(
                    "Не найден серверный .conf-файл с этим peer. Удаление отменено без изменений.");
            }

            var originalConfig = ExecuteDocker(client, $"exec -i {containerName} cat {configPath}");
            if (string.IsNullOrWhiteSpace(originalConfig))
                return OperationResult.Fail("Не удалось прочитать серверную конфигурацию. Удаление не выполнено.");

            if (!TryRemovePeerBlock(originalConfig, clientId, out var updatedConfig))
            {
                return OperationResult.Fail(
                    "Peer не найден в серверном .conf-файле. Удаление отменено без изменений.");
            }

            var clientsTablePath = DetectClientsTablePath(client, containerName);
            string? originalClientsTable = null;
            string? updatedClientsTable = null;

            if (clientsTablePath is not null)
            {
                originalClientsTable = ExecuteDocker(client, $"exec -i {containerName} cat {clientsTablePath}");
                if (!string.IsNullOrWhiteSpace(originalClientsTable))
                {
                    try
                    {
                        var root = JsonNode.Parse(originalClientsTable);
                        if (root is not null && RemoveFromClientsTable(root, clientId))
                        {
                            updatedClientsTable = root.ToJsonString(new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                        }
                    }
                    catch (JsonException)
                    {
                        // Отзыв VPN-доступа важнее служебного имени. Неизвестный clientsTable не меняем.
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var configBackupPath = $"{configPath}.dashboard-backup-{timestamp}";

            var backupConfigResult = CopyFileInContainer(
                client,
                containerName,
                configPath,
                configBackupPath);

            if (!backupConfigResult.Success)
                return OperationResult.Fail($"Не удалось создать резервную копию конфигурации: {backupConfigResult.ErrorMessage}");

            string? clientsTableBackupPath = null;
            if (clientsTablePath is not null && originalClientsTable is not null)
            {
                clientsTableBackupPath = $"{clientsTablePath}.dashboard-backup-{timestamp}";
                var backupTableResult = CopyFileInContainer(
                    client,
                    containerName,
                    clientsTablePath,
                    clientsTableBackupPath);

                if (!backupTableResult.Success)
                {
                    return OperationResult.Fail(
                        $"Не удалось создать резервную копию clientsTable: {backupTableResult.ErrorMessage}");
                }
            }

            var writeConfigResult = WriteTextFileToContainer(
                client,
                containerName,
                configPath,
                updatedConfig);

            if (!writeConfigResult.Success)
                return writeConfigResult;

            if (clientsTablePath is not null && updatedClientsTable is not null)
            {
                var writeTableResult = WriteTextFileToContainer(
                    client,
                    containerName,
                    clientsTablePath,
                    updatedClientsTable);

                if (!writeTableResult.Success)
                {
                    RestoreBackup(client, containerName, configBackupPath, configPath);
                    return OperationResult.Fail(
                        $"Не удалось обновить clientsTable. Конфигурация восстановлена из резервной копии. {writeTableResult.ErrorMessage}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var removeLive = ExecuteDockerCommand(
                client,
                $"exec -i {containerName} {runtime.Value.Executable} set {runtime.Value.InterfaceName} peer {clientId} remove");

            if (!removeLive.Success)
            {
                RestoreBackup(client, containerName, configBackupPath, configPath);

                if (clientsTablePath is not null && clientsTableBackupPath is not null)
                    RestoreBackup(client, containerName, clientsTableBackupPath, clientsTablePath);

                return OperationResult.Fail(
                    "Не удалось удалить peer из активного интерфейса. Файлы конфигурации восстановлены из резервных копий. " +
                    ValueOrFallback(removeLive.Error, "Неизвестная ошибка wg/awg."));
            }

            client.Disconnect();

            return OperationResult.Ok(
                "Доступ отозван. Перед изменением автоматически созданы резервные копии конфигурации на сервере.");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Операция отменена.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(GetFriendlyError(ex));
        }
    }

    private static bool RenameInDocument(JsonNode root, string clientId, string newName)
    {
        if (root is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is not JsonObject client)
                    continue;

                var id = client["clientId"]?.GetValue<string>();
                if (!string.Equals(id, clientId, StringComparison.Ordinal))
                    continue;

                if (client["userData"] is not JsonObject userData)
                {
                    userData = new JsonObject();
                    client["userData"] = userData;
                }

                userData["clientName"] = newName;
                return true;
            }

            return false;
        }

        if (root is JsonObject legacy)
        {
            if (!legacy.TryGetPropertyValue(clientId, out var node) || node is not JsonObject client)
                return false;

            if (client["userData"] is JsonObject userData)
                userData["clientName"] = newName;
            else
                client["clientName"] = newName;

            return true;
        }

        return false;
    }

    private static bool RemoveFromClientsTable(JsonNode root, string clientId)
    {
        if (root is JsonArray array)
        {
            for (var index = array.Count - 1; index >= 0; index--)
            {
                if (array[index] is not JsonObject item)
                    continue;

                var id = item["clientId"]?.GetValue<string>();
                if (!string.Equals(id, clientId, StringComparison.Ordinal))
                    continue;

                array.RemoveAt(index);
                return true;
            }

            return false;
        }

        if (root is JsonObject legacy)
            return legacy.Remove(clientId);

        return false;
    }

    private static bool ReplaceClientIdInClientsTable(JsonNode root, string oldClientId, string newClientId)
    {
        if (root is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is not JsonObject item)
                    continue;

                var id = item["clientId"]?.GetValue<string>();
                if (!string.Equals(id, oldClientId, StringComparison.Ordinal))
                    continue;

                item["clientId"] = newClientId;
                return true;
            }

            return false;
        }

        if (root is JsonObject legacy)
        {
            if (!legacy.TryGetPropertyValue(oldClientId, out var node) || node is null)
                return false;

            var preserved = node.DeepClone();
            legacy.Remove(oldClientId);
            legacy[newClientId] = preserved;
            return true;
        }

        return false;
    }

    private static bool TryGetPeerValues(
        string config,
        string clientId,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = config.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            if (!string.Equals(lines[index].Trim(), "[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            var end = index + 1;
            var candidate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (end < lines.Length && !IsSectionHeader(lines[end]))
            {
                var line = lines[end].Trim();
                var equals = line.IndexOf('=');
                if (equals > 0)
                {
                    var key = line[..equals].Trim();
                    var value = line[(equals + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(key))
                        candidate[key] = value;
                }

                end++;
            }

            if (candidate.TryGetValue("PublicKey", out var publicKey) &&
                string.Equals(publicKey, clientId, StringComparison.Ordinal))
            {
                values = candidate;
                return true;
            }

            index = end;
        }

        return false;
    }

    private static bool TryGetClientIpv4(string allowedIps, out string clientAddress)
    {
        clientAddress = string.Empty;

        foreach (var part in allowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cidr = part.Split('/', 2, StringSplitOptions.TrimEntries);
            if (cidr.Length != 2 || cidr[1] != "32")
                continue;

            if (IPAddress.TryParse(cidr[0], out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                clientAddress = cidr[0];
                return true;
            }
        }

        return false;
    }

    private static bool TryReplacePeerPublicKey(
        string config,
        string oldClientId,
        string newClientId,
        out string updatedConfig)
    {
        var normalized = config.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        var index = 0;

        while (index < lines.Count)
        {
            if (!string.Equals(lines[index].Trim(), "[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            var end = index + 1;
            var publicKeyLine = -1;
            var matches = false;

            while (end < lines.Count && !IsSectionHeader(lines[end]))
            {
                var line = lines[end].Trim();
                var equals = line.IndexOf('=');
                if (equals > 0)
                {
                    var key = line[..equals].Trim();
                    var value = line[(equals + 1)..].Trim();
                    if (string.Equals(key, "PublicKey", StringComparison.OrdinalIgnoreCase))
                    {
                        publicKeyLine = end;
                        matches = string.Equals(value, oldClientId, StringComparison.Ordinal);
                    }
                }

                end++;
            }

            if (matches && publicKeyLine >= 0)
            {
                var originalLine = lines[publicKeyLine];
                var indentLength = originalLine.Length - originalLine.TrimStart().Length;
                var indent = indentLength > 0 ? originalLine[..indentLength] : string.Empty;
                lines[publicKeyLine] = $"{indent}PublicKey = {newClientId}";
                updatedConfig = string.Join("\n", lines).TrimEnd() + "\n";
                return true;
            }

            index = end;
        }

        updatedConfig = config;
        return false;
    }

    private static string? DetectServerConfigPath(
        SshClient client,
        string containerName,
        RuntimeInfo runtime)
    {
        string[] candidates = runtime.Executable == "awg"
            ? ["/opt/amnezia/awg/awg0.conf", "/opt/amnezia/awg/wg0.conf"]
            : containerName.Contains("awg", StringComparison.OrdinalIgnoreCase)
                ? ["/opt/amnezia/awg/wg0.conf", "/opt/amnezia/awg/awg0.conf"]
                : ["/opt/amnezia/wireguard/wg0.conf"];

        foreach (var path in candidates)
        {
            if (FileExistsInContainer(client, containerName, path))
                return path;
        }

        return null;
    }

    private static Dictionary<string, string> ReadInterfaceValues(string config)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inInterface = false;

        foreach (var rawLine in config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inInterface = string.Equals(line, "[Interface]", StringComparison.OrdinalIgnoreCase);
                if (!inInterface && values.Count > 0)
                    break;
                continue;
            }

            if (!inInterface)
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                values[key] = value;
        }

        return values;
    }

    private static string? FindFreeClientAddress(string addressValue, string config)
    {
        var primaryAddress = addressValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Contains('.'));

        if (primaryAddress is null || !TryParseIpv4Cidr(primaryAddress, out var serverIp, out var prefix))
            return null;

        if (prefix is < 8 or > 30)
            return null;

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = serverIp & mask;
        var broadcast = network | ~mask;

        var used = new HashSet<uint> { serverIp };
        var regex = new Regex(@"AllowedIPs\s*=\s*(\d{1,3}(?:\.\d{1,3}){3})/\d{1,2}", RegexOptions.IgnoreCase);
        foreach (Match match in regex.Matches(config))
        {
            if (TryParseIpv4(match.Groups[1].Value, out var usedIp))
                used.Add(usedIp);
        }

        // Не перебираем гигантские подсети целиком: Amnezia обычно использует /24.
        var maxCandidates = 65536u;
        var first = network + 1;
        var last = broadcast - 1;
        var checkedCount = 0u;

        for (var candidate = first; candidate <= last && checkedCount < maxCandidates; candidate++, checkedCount++)
        {
            if (candidate == network || candidate == broadcast || used.Contains(candidate))
                continue;

            return Ipv4ToString(candidate);
        }

        return null;
    }

    private static bool TryParseIpv4Cidr(string value, out uint address, out int prefix)
    {
        address = 0;
        prefix = 0;

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out prefix))
            return false;

        return TryParseIpv4(parts[0], out address);
    }

    private static bool TryParseIpv4(string value, out uint address)
    {
        address = 0;
        if (!IPAddress.TryParse(value, out var ip))
            return false;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        address = ((uint)bytes[0] << 24) |
                  ((uint)bytes[1] << 16) |
                  ((uint)bytes[2] << 8) |
                  bytes[3];
        return true;
    }

    private static string Ipv4ToString(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";

    private static string ReadPresharedKey(
        SshClient client,
        string containerName,
        string configPath,
        string config)
    {
        var match = Regex.Match(
            config,
            @"(?im)^\s*PresharedKey\s*=\s*([^\s#;]+)\s*$");

        if (match.Success)
            return match.Groups[1].Value.Trim();

        var keyPath = configPath.StartsWith("/opt/amnezia/awg/", StringComparison.Ordinal)
            ? "/opt/amnezia/awg/wireguard_psk.key"
            : "/opt/amnezia/wireguard/wireguard_psk.key";

        if (!FileExistsInContainer(client, containerName, keyPath))
            return string.Empty;

        return ExecuteDocker(client, $"exec -i {containerName} cat {keyPath}").Trim();
    }

    private static int? ReadListenPort(Dictionary<string, string> interfaceValues)
    {
        if (!interfaceValues.TryGetValue("ListenPort", out var text))
            return null;

        return int.TryParse(text.Trim(), out var port) && port is >= 1 and <= 65535
            ? port
            : null;
    }

    private static string DefaultClientsTablePath(string configPath) =>
        configPath.StartsWith("/opt/amnezia/awg/", StringComparison.Ordinal)
            ? "/opt/amnezia/awg/clientsTable"
            : "/opt/amnezia/wireguard/clientsTable";

    private static bool FileExistsInContainer(
        SshClient client,
        string containerName,
        string path)
    {
        if (!IsSafeAmneziaPath(path))
            return false;

        var result = ExecuteDockerCommand(
            client,
            $"exec -i {containerName} sh -c \"test -f '{path}' && echo yes\"");

        return result.Success &&
               string.Equals(result.Output.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AddToClientsTable(JsonNode root, string clientId, string clientName)
    {
        if (root is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is JsonObject item &&
                    string.Equals(item["clientId"]?.GetValue<string>(), clientId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            array.Add(new JsonObject
            {
                ["clientId"] = clientId,
                ["userData"] = new JsonObject
                {
                    ["clientName"] = clientName,
                    ["creationDate"] = DateTime.Now.ToString("O")
                }
            });
            return true;
        }

        if (root is JsonObject legacy)
        {
            if (legacy.ContainsKey(clientId))
                return false;

            legacy[clientId] = new JsonObject
            {
                ["clientName"] = clientName
            };
            return true;
        }

        return false;
    }

    private static void RemoveFileInContainer(
        SshClient client,
        string containerName,
        string path)
    {
        if (!IsSafeAmneziaPath(path))
            return;

        ExecuteDockerCommand(
            client,
            $"exec -i {containerName} sh -c \"rm -f -- '{path}'\"");
    }

    private static DockerCommandResult SyncRuntimeFromConfig(
        SshClient client,
        string containerName,
        RuntimeInfo runtime,
        string configPath)
    {
        if (!IsSafeAmneziaPath(configPath))
            return new DockerCommandResult(false, string.Empty, "Некорректный путь конфигурации.");

        return ExecuteDockerCommand(
            client,
            $"exec -i {containerName} bash -c '{runtime.Executable} syncconf {runtime.InterfaceName} <({runtime.Executable}-quick strip {configPath})'");
    }

    private static string BuildClientConfig(
        string host,
        int port,
        string clientAddress,
        string privateKey,
        string serverPublicKey,
        string presharedKey,
        Dictionary<string, string> serverInterfaceValues)
    {
        string[] awgKeys =
        [
            "Jc", "Jmin", "Jmax",
            "S1", "S2", "S3", "S4",
            "H1", "H2", "H3", "H4",
            "I1", "I2", "I3", "I4", "I5",
            "HeaderProtectionKey",
            "ContentPaddingAddition",
            "RekeyAfterTime",
            "RekeyTimeout",
            "RejectAfterTime",
            "KeepaliveTimeout",
            "MaxHandshakeAttempts",
            "RandomTrailers",
            "DisableCookies"
        ];

        var builder = new StringBuilder();
        builder.AppendLine("[Interface]");
        builder.AppendLine($"Address = {clientAddress}/32");
        builder.AppendLine($"PrivateKey = {privateKey}");

        foreach (var key in awgKeys)
        {
            if (serverInterfaceValues.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine($"{key} = {value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[Peer]");
        builder.AppendLine($"PublicKey = {serverPublicKey}");
        builder.AppendLine($"PresharedKey = {presharedKey}");
        builder.AppendLine("AllowedIPs = 0.0.0.0/0, ::/0");
        builder.AppendLine($"Endpoint = {host}:{port}");

        var hasAwg3Parameters =
            serverInterfaceValues.ContainsKey("HeaderProtectionKey") ||
            serverInterfaceValues.ContainsKey("ContentPaddingAddition") ||
            serverInterfaceValues.ContainsKey("S3");

        builder.AppendLine($"PersistentKeepalive = {(hasAwg3Parameters ? "25-35" : "25")}");

        return builder.ToString();
    }

    private static bool TryRemovePeerBlock(
        string config,
        string clientId,
        out string updatedConfig)
    {
        var normalized = config.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var result = new List<string>();
        var removed = false;
        var index = 0;

        while (index < lines.Length)
        {
            if (!string.Equals(lines[index].Trim(), "[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(lines[index]);
                index++;
                continue;
            }

            var end = index + 1;
            while (end < lines.Length && !IsSectionHeader(lines[end]))
                end++;

            var matchesPeer = false;
            for (var peerLine = index + 1; peerLine < end; peerLine++)
            {
                var line = lines[peerLine].Trim();
                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                var key = line[..equalsIndex].Trim();
                var value = line[(equalsIndex + 1)..].Trim();

                if (string.Equals(key, "PublicKey", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(value, clientId, StringComparison.Ordinal))
                {
                    matchesPeer = true;
                    break;
                }
            }

            if (matchesPeer)
            {
                removed = true;
                index = end;

                // Не оставляем несколько пустых строк в месте удалённого peer-блока.
                while (index < lines.Length &&
                       string.IsNullOrWhiteSpace(lines[index]) &&
                       result.Count > 0 &&
                       string.IsNullOrWhiteSpace(result[^1]))
                {
                    index++;
                }

                continue;
            }

            for (var peerLine = index; peerLine < end; peerLine++)
                result.Add(lines[peerLine]);

            index = end;
        }

        updatedConfig = string.Join("\n", result).TrimEnd() + "\n";
        return removed;
    }

    private static bool IsSectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']';
    }

    private static RuntimeInfo? DetectRuntime(SshClient client, string containerName)
    {
        foreach (var executable in new[] { "awg", "wg" })
        {
            var result = ExecuteDockerCommand(
                client,
                $"exec -i {containerName} {executable} show interfaces");

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                continue;

            var interfaceName = result.Output
                .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(interfaceName) && IsSafeInterfaceName(interfaceName))
                return new RuntimeInfo(executable, interfaceName);
        }

        return null;
    }

    private static string? FindPeerConfigPath(
        SshClient client,
        string containerName,
        string clientId)
    {
        string[] preferredPaths =
        [
            "/opt/amnezia/awg/awg0.conf",
            "/opt/amnezia/awg/wg0.conf",
            "/opt/amnezia/wireguard/wg0.conf"
        ];

        foreach (var path in preferredPaths)
        {
            if (!FileExistsInContainer(client, containerName, path))
                continue;

            var config = ExecuteDocker(client, $"exec -i {containerName} cat {path}");
            if (TryGetPeerValues(config, clientId, out _))
                return path;
        }

        var result = ExecuteDockerCommand(
            client,
            $"exec -i {containerName} sh -c \"grep -rlF -- '{clientId}' /opt/amnezia/awg /opt/amnezia/wireguard 2>/dev/null\"");

        if (!result.Success)
            return null;

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .FirstOrDefault(path =>
                path.EndsWith(".conf", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(".dashboard-backup-", StringComparison.OrdinalIgnoreCase) &&
                IsSafeAmneziaPath(path));
    }

    private static string? DetectClientsTablePath(SshClient client, string containerName)
    {
        string[] candidates =
        [
            "/opt/amnezia/awg/clientsTable",
            "/opt/amnezia/wireguard/clientsTable",
            "/opt/amnezia/openvpn/clientsTable",
            "/opt/amnezia/xray/clientsTable"
        ];

        foreach (var path in candidates)
        {
            var result = ExecuteDockerCommand(
                client,
                $"exec -i {containerName} sh -c \"test -f '{path}' && echo yes\"");

            if (result.Success && string.Equals(result.Output.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }

    private static OperationResult CopyFileInContainer(
        SshClient client,
        string containerName,
        string sourcePath,
        string destinationPath)
    {
        if (!IsSafeAmneziaPath(sourcePath) || !IsSafeAmneziaPath(destinationPath))
            return OperationResult.Fail("Некорректный путь файла в контейнере.");

        var result = ExecuteDockerCommand(
            client,
            $"exec -i {containerName} sh -c \"cp -- '{sourcePath}' '{destinationPath}'\"");

        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(ValueOrFallback(result.Error, "Команда cp завершилась с ошибкой."));
    }

    private static void RestoreBackup(
        SshClient client,
        string containerName,
        string backupPath,
        string targetPath)
    {
        if (!IsSafeAmneziaPath(backupPath) || !IsSafeAmneziaPath(targetPath))
            return;

        ExecuteDockerCommand(
            client,
            $"exec -i {containerName} sh -c \"cp -- '{backupPath}' '{targetPath}'\"");
    }

    private static OperationResult WriteTextFileToContainer(
        SshClient client,
        string containerName,
        string path,
        string text)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return WriteFileToContainer(client, containerName, path, base64);
    }

    private static OperationResult WriteFileToContainer(
        SshClient client,
        string containerName,
        string path,
        string base64)
    {
        if (!IsSafeAmneziaPath(path))
            return OperationResult.Fail("Некорректный путь файла в контейнере.");

        var shellCommand =
            $"printf '%s' '{base64}' | base64 -d | docker exec -i {containerName} sh -c 'cat > {path}'";

        using var direct = client.CreateCommand(shellCommand);
        direct.CommandTimeout = TimeSpan.FromSeconds(10);
        direct.Execute();
        if (direct.ExitStatus == 0)
            return OperationResult.Ok();

        var sudoCommand =
            $"printf '%s' '{base64}' | base64 -d | sudo -n docker exec -i {containerName} sh -c 'cat > {path}'";

        using var sudo = client.CreateCommand(sudoCommand);
        sudo.CommandTimeout = TimeSpan.FromSeconds(10);
        sudo.Execute();
        if (sudo.ExitStatus == 0)
            return OperationResult.Ok();

        var error = string.IsNullOrWhiteSpace(sudo.Error)
            ? direct.Error
            : sudo.Error;

        return OperationResult.Fail(
            string.IsNullOrWhiteSpace(error)
                ? "Не удалось сохранить файл на сервере."
                : error.Trim());
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

    private static string ExecuteDocker(SshClient client, string arguments)
    {
        var result = ExecuteDockerCommand(client, arguments);
        return result.Success ? result.Output : string.Empty;
    }

    private static DockerCommandResult ExecuteDockerCommand(
        SshClient client,
        string arguments)
    {
        var direct = ExecuteDetailed(client, $"docker {arguments} 2>/dev/null");
        if (direct.Success)
            return direct;

        var sudo = ExecuteDetailed(client, $"sudo -n docker {arguments} 2>/dev/null");
        if (sudo.Success)
            return sudo;

        return string.IsNullOrWhiteSpace(sudo.Error) ? direct : sudo;
    }

    private static DockerCommandResult ExecuteDetailed(
        SshClient client,
        string commandText)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromSeconds(10);
        var output = command.Execute().Trim();

        return new DockerCommandResult(
            command.ExitStatus == 0,
            output,
            command.Error?.Trim() ?? string.Empty);
    }

    private static bool IsSafeDockerName(string value) =>
        Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$");

    private static bool IsSafeInterfaceName(string value) =>
        Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$");

    private static bool IsSafePeerKey(string value) =>
        value.Length is >= 20 and <= 100 &&
        Regex.IsMatch(value, "^[A-Za-z0-9+/=]+$");

    private static bool IsSafeAmneziaPath(string value) =>
        value.StartsWith("/opt/amnezia/", StringComparison.Ordinal) &&
        Regex.IsMatch(value, "^/opt/amnezia/[A-Za-z0-9_./-]+$");

    private static string ValueOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

    private readonly record struct RuntimeInfo(string Executable, string InterfaceName);

    private readonly record struct DockerCommandResult(bool Success, string Output, string Error);
}
