using Atlas.Email.Models;

namespace Atlas.Email.Abstractions;

/// <summary>
/// Represents the minimal data about a mail account needed by Atlas.Email providers and controllers.
/// Implement this interface on your application's account entity to connect it to the Atlas.Email layer.
/// </summary>
public interface IMailAccountRecord
{
    string? Id { get; set; }
    string? TenantId { get; set; }
    string Email { get; set; }
    MailProviderType ProviderType { get; set; }
    string? EncryptedSettings { get; set; }
    bool IsActive { get; set; }
    string? Name { get; set; }
}
