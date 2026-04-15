using Atlas.Email.Models;

namespace Atlas.Email.Abstractions;

/// <summary>
/// Simple in-memory implementation of IMailAccountRecord used to construct new account records
/// inside Atlas.Email controllers before handing them off to IMailAccountStore.
/// </summary>
public class MailAccountRecord : IMailAccountRecord
{
    public string? Id { get; set; }
    public string? TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public MailProviderType ProviderType { get; set; }
    public string? EncryptedSettings { get; set; }
    public bool IsActive { get; set; }
    public string? Name { get; set; }
}
