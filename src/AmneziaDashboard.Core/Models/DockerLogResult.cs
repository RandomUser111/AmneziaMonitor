namespace AmneziaDashboard.Core.Models;

public class DockerLogResult
{
    public bool Success { get; set; }

    public bool IsUnavailable { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string LoggingDriver { get; set; } = string.Empty;

    public static DockerLogResult Ok(string content, string loggingDriver = "") => new()
    {
        Success = true,
        Content = content,
        LoggingDriver = loggingDriver
    };

    public static DockerLogResult Unavailable(string message, string loggingDriver = "") => new()
    {
        Success = false,
        IsUnavailable = true,
        ErrorMessage = message,
        LoggingDriver = loggingDriver
    };

    public static DockerLogResult Fail(string message, string loggingDriver = "") => new()
    {
        Success = false,
        ErrorMessage = message,
        LoggingDriver = loggingDriver
    };
}
