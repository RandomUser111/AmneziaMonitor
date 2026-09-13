namespace AmneziaDashboard.Core.Models;

public class CreateClientResult
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientAddress { get; set; } = string.Empty;

    public string ConfigText { get; set; } = string.Empty;

    public static CreateClientResult Ok(
        string clientId,
        string clientAddress,
        string configText,
        string message = "") => new()
    {
        Success = true,
        ClientId = clientId,
        ClientAddress = clientAddress,
        ConfigText = configText,
        Message = message
    };

    public static CreateClientResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}
