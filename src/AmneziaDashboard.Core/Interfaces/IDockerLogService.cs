using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IDockerLogService
{
    Task<DockerLogResult> GetLogsAsync(
        ServerConnection connection,
        string containerName,
        int tailLines = 200,
        CancellationToken cancellationToken = default);
}
