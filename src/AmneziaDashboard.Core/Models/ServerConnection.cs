namespace AmneziaDashboard.Core.Models;

public class ServerConnection
{
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = "root";

    public string Password { get; set; } = string.Empty;
}