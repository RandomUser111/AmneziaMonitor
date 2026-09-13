namespace AmneziaDashboard.Core.Models;

public class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Мой сервер";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = "root";

    public bool RememberPassword { get; set; }

    public bool AutoConnect { get; set; }

    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
}
