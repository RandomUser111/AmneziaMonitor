using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IServerProfileStore
{
    Task<IReadOnlyList<ServerProfile>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}
