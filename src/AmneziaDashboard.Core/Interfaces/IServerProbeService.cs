using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IServerProbeService
{
    Task<ServerProbeResult> ProbeAsync(
        ServerConnection connection,
        CancellationToken cancellationToken = default);
}