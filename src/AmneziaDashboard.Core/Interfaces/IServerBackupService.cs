using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IServerBackupService
{
    Task<ServerBackupResult> CreateFullBackupAsync(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ServerBackupResult> RestoreFullBackupAsync(
        ServerConnection connection,
        string localPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
