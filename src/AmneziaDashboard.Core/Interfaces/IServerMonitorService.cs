using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IServerMonitorService
{
    Task<ServerMonitorSnapshot> ReadAsync(
        ServerConnection connection,
        CancellationToken cancellationToken = default);
}
