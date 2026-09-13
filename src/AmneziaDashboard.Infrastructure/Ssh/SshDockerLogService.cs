using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public class SshDockerLogService : IDockerLogService
{
    public Task<DockerLogResult> GetLogsAsync(
        ServerConnection connection,
        string containerName,
        int tailLines = 200,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLogs(connection, containerName, tailLines, cancellationToken), cancellationToken);
    }

    private static DockerLogResult ReadLogs(
        ServerConnection connection,
        string containerName,
        int tailLines,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return DockerLogResult.Fail("Некорректное имя Docker-контейнера.");

        tailLines = Math.Clamp(tailLines, 20, 2000);

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return DockerLogResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var loggingDriver = ReadLoggingDriver(client, containerName);

            // Официальные контейнеры Amnezia часто запускаются с --log-driver none.
            // В таком режиме stdout/stderr намеренно не сохраняются Docker-ом, поэтому
            // восстановить их через docker logs невозможно.
            if (loggingDriver.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                client.Disconnect();
                return DockerLogResult.Unavailable(
                    "Для этого контейнера журнал Docker отключён настройкой log-driver=none. " +
                    "Это штатная конфигурация Amnezia: stdout/stderr контейнера не сохраняются, " +
                    "поэтому получить их задним числом невозможно. Используйте вкладку «События» " +
                    "для журнала операций Amnezia Monitor.",
                    loggingDriver);
            }

            var commandText =
                $"docker logs --timestamps --tail {tailLines} {containerName} 2>&1 || " +
                $"sudo docker logs --timestamps --tail {tailLines} {containerName} 2>&1";

            using var command = client.CreateCommand(commandText);
            command.CommandTimeout = TimeSpan.FromSeconds(20);
            var output = command.Execute() ?? string.Empty;

            if (command.ExitStatus != 0)
            {
                var error = string.IsNullOrWhiteSpace(command.Error)
                    ? output.Trim()
                    : command.Error.Trim();

                if (error.Contains("configured logging driver does not support reading", StringComparison.OrdinalIgnoreCase))
                {
                    client.Disconnect();
                    var driverText = string.IsNullOrWhiteSpace(loggingDriver) ? "неизвестный" : loggingDriver;
                    return DockerLogResult.Unavailable(
                        $"Docker logging driver «{driverText}» не поддерживает чтение через docker logs. " +
                        "Amnezia Monitor не будет считать это ошибкой контейнера. " +
                        "Если журнал не хранится самим драйвером, получить прошлые сообщения невозможно.",
                        loggingDriver);
                }

                return DockerLogResult.Fail(
                    string.IsNullOrWhiteSpace(error)
                        ? "Не удалось прочитать Docker logs."
                        : error,
                    loggingDriver);
            }

            client.Disconnect();
            return DockerLogResult.Ok(output.TrimEnd(), loggingDriver);
        }
        catch (OperationCanceledException)
        {
            return DockerLogResult.Fail("Операция отменена.");
        }
        catch (Exception ex)
        {
            return DockerLogResult.Fail(GetFriendlyError(ex));
        }
    }

    private static string ReadLoggingDriver(SshClient client, string containerName)
    {
        var commandText =
            $"docker inspect -f '{{{{.HostConfig.LogConfig.Type}}}}' {containerName} 2>/dev/null || " +
            $"sudo docker inspect -f '{{{{.HostConfig.LogConfig.Type}}}}' {containerName} 2>/dev/null";

        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromSeconds(10);
        var output = command.Execute() ?? string.Empty;

        return command.ExitStatus == 0 ? output.Trim() : string.Empty;
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

    private static bool IsSafeDockerName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$");

    private static string GetFriendlyError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("permission denied") || message.Contains("authentication"))
            return "Ошибка авторизации. Проверьте SSH-доступ.";

        if (message.Contains("timed out") || message.Contains("timeout"))
            return "Сервер не ответил за отведённое время.";

        if (message.Contains("connection refused"))
            return "SSH-соединение отклонено сервером.";

        return ex.Message;
    }
}
