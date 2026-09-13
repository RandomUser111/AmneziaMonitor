using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface ISecretStore
{
    bool IsAvailable { get; }

    string BackendName { get; }

    Task<string?> GetPasswordAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetPasswordAsync(
        string profileId,
        string password,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeletePasswordAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}
