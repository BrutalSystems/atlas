namespace Atlas.Settings;

/// <summary>
/// Used for OAuth anonymous callbacks (see Maia QboController)
/// </summary>
public class AnonymousCallbackLink
{
    public string? AuthUserId { get; set; } 
    public string? UserEmail { get; set; }
    public string? TenantId { get; set; }
    public string? Database { get; set; }
    public string ReturnUrl { get; set; } = string.Empty;
    
    public string? RowId { get; set; }
}