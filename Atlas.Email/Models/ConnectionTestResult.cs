namespace Atlas.Email.Models;

/// <summary>
/// Represents the result of testing a connection to a mail provider.
/// </summary>
public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Details { get; set; }
}
