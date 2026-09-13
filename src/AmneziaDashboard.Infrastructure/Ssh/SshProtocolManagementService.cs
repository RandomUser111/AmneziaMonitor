using System.Text.RegularExpressions;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Renci.SshNet;

namespace AmneziaDashboard.Infrastructure.Ssh;

public class SshProtocolManagementService : IProtocolManagementService
{
    public Task<OperationResult> StartContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ControlContainer(
            connection,
            containerName,
            "start",
            "Контейнер запущен.",
            cancellationToken), cancellationToken);
    }

    public Task<OperationResult> StopContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ControlContainer(
            connection,
            containerName,
            "stop",
            "Контейнер остановлен.",
            cancellationToken), cancellationToken);
    }

    public Task<OperationResult> RestartContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ControlContainer(
            connection,
            containerName,
            "restart",
            "Контейнер перезапущен.",
            cancellationToken), cancellationToken);
    }

    private static OperationResult ControlContainer(
        ServerConnection connection,
        string containerName,
        string action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDockerName(containerName))
            return OperationResult.Fail("Некорректное имя Docker-контейнера.");

        if (action is not ("start" or "stop" or "restart"))
            return OperationResult.Fail("Некорректная операция Docker.");

        try
        {
            using var client = CreateClient(connection);
            client.Connect();

            if (!client.IsConnected)
                return OperationResult.Fail("Не удалось установить SSH-соединение.");

            cancellationToken.ThrowIfCancellationRequested();

            var result = ExecuteDockerCommand(client, $"{action} {containerName}");
            if (!result.Success)
                return OperationResult.Fail(ValueOrFallback(result.Error, $"Команда docker {action} завершилась ошибкой."));

            var inspect = ExecuteDockerCommand(
                client,
                $"inspect -f '{{{{.State.Running}}}}' {containerName}");

            if (!inspect.Success)
                return OperationResult.Fail(ValueOrFallback(inspect.Error, "Не удалось проверить состояние контейнера после операции."));

            var isRunning = inspect.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            var expectedStateReached = action switch
            {
                "start" => isRunning,
                "stop" => !isRunning,
                "restart" => isRunning,
                _ => false
            };

            if (!expectedStateReached)
            {
                return OperationResult.Fail(
                    "Docker выполнил команду, но итоговое состояние контейнера отличается от ожидаемого.");
            }

            client.Disconnect();
            return OperationResult.Ok(successMessage);
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

    private static CommandResult ExecuteDockerCommand(SshClient client, string dockerArgs)
    {
        var command = $"docker {dockerArgs} 2>&1 || sudo docker {dockerArgs} 2>&1";
        return ExecuteCommand(client, command);
    }

    private static CommandResult ExecuteCommand(SshClient client, string commandText)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromSeconds(25);

        var output = command.Execute() ?? string.Empty;
        var exitStatus = command.ExitStatus;
        var error = command.Error?.Trim() ?? string.Empty;
        output = output.Trim();

        return new CommandResult
        {
            Success = exitStatus == 0,
            ExitStatus = exitStatus ?? -1,
            Output = output,
            Error = string.IsNullOrWhiteSpace(error) ? output : error
        };
    }

    private static bool IsSafeDockerName(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$");
    }

    private static string ValueOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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

    private sealed class CommandResult
    {
        public bool Success { get; init; }

        public int ExitStatus { get; init; }

        public string Output { get; init; } = string.Empty;

        public string Error { get; init; } = string.Empty;
    }
}
