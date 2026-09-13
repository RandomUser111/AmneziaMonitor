using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IClientManagementService
{
    Task<OperationResult> RenameClientAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        string newName,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RevokeClientAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        CancellationToken cancellationToken = default);

    Task<CreateClientResult> CreateClientAsync(
        ServerConnection connection,
        string containerName,
        string clientName,
        CancellationToken cancellationToken = default);

    Task<CreateClientResult> RestoreClientConfigAsync(
        ServerConnection connection,
        string containerName,
        string clientId,
        string clientName,
        CancellationToken cancellationToken = default);
}
