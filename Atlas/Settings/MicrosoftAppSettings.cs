namespace Atlas.Settings;

public class MicrosoftAppSettings
{
    /// <summary>
    /// Gets or sets the Microsoft Application (Client) ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Microsoft Application (Client) Secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Tenant ID for the Microsoft Application.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}