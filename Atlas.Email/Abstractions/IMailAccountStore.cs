using Atlas.Email.Models;

namespace Atlas.Email.Abstractions;

/// <summary>
/// Abstraction for persisting and retrieving mail account records.
/// Implement this in your application to wire Atlas.Email controllers to your data store.
/// </summary>
public interface IMailAccountStore
{
    Task<IMailAccountRecord?> GetByIdAsync(string accountId, CancellationToken ct = default);
    Task<IMailAccountRecord?> GetByEmailAsync(string email, MailProviderType providerType, CancellationToken ct = default);

    /// <summary>Create or update the account record (upsert based on Id).</summary>
    Task SaveAsync(IMailAccountRecord account, CancellationToken ct = default);
}
