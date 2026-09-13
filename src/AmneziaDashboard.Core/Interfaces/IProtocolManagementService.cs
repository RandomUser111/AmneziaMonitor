using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IProtocolManagementService
{
    Task<OperationResult> StartContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default);

    Task<OperationResult> StopContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RestartContainerAsync(
        ServerConnection connection,
        string containerName,
        CancellationToken cancellationToken = default);
}
