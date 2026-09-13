namespace AmneziaDashboard.Core.Models;

public sealed class ServerBackupResult : OperationResult
{
    public string BackupPath { get; set; } = string.Empty;
    public string SafetyBackupPath { get; set; } = string.Empty;

    public static ServerBackupResult Ok(string path, string message = "") => new()
    {
        Success = true,
        BackupPath = path,
        Message = message
    };

    public new static ServerBackupResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}
