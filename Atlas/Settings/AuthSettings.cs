namespace Atlas.Settings;

public class AuthSettings
{
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public bool ValidateIssuer { get; set; } = true;
    public string TenantIdClaim { get; set; } = "tenantId";
    public string DatabaseClaim { get; set; } = "database";
    public string[]? AuthorizedDomains { get; set; } = [];
    public bool AutoEnableNewUsers { get; set; } = false;
    public string? InviteUrl { get; set; } = "http://localhost:8080";
    public int InviteExpirationMinutes { get; set; } = 1;
    public string? TenantlessAuthority { get; set; }

    /// <summary>
    /// Used for anonymous callbacks that should link to a user
    /// </summary>
    public string[] AnonymousCallbackParam { get; set; } = ["state"];

    public string? SuperUserEmail { get; set; }
    public string? SuperUserRole { get; set; }

    // used when API runs in MockAuthentication mode (auth for development & testing)
    public bool UseMockAuthentication { get; set; } = false;
    public string MockTenantId { get; set; } = "00000000-0000-0000-0000-00000000000A";
    public string MockTenantName { get; set; } = "Mock, LLC.";
    public string MockUserId { get; set; } = "00000000-0000-0000-0000-00000000000A";
    public string MockAuthUserId { get; set; } = "mock-auth-user-id";
    public string MockUserName { get; set; } = "Mock User";
    public string MockUserEmail { get; set; } = "mock@1023ventures.com";
    public string MockRoleName { get; set; } = "Mock User";
    public string MockSuperUserRole { get; set; } = "Super User"; // needed???
    public string MockPermissions { get; set; } = "[\"User.ReadAll=true\",\"User.Upsert=true\",\"User.ReadAll=true\"]";
}