namespace AmneziaDashboard.Core.Models;

public sealed class BackupProgress
{
    public int Percent { get; init; }
    public string Stage { get; init; } = string.Empty;
}
