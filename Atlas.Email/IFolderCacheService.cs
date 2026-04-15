namespace Atlas.Email;

/// <summary>
/// Service for caching folder ID mappings to avoid repeated API calls to mail providers.
/// Provides a two-tier cache: in-memory for fast access and database for persistence.
/// </summary>
public interface IFolderCacheService
{
    /// <summary>
    /// Gets the folder ID for a given folder path from cache.
    /// </summary>
    Task<string?> GetFolderIdAsync(string tenantId, string accountId, string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the folder ID for a given folder path in cache.
    /// Updates both in-memory cache and database.
    /// </summary>
    Task SetFolderIdAsync(string tenantId, string accountId, string folderPath, string folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached folders for a specific account.
    /// Clears both in-memory cache and database entries.
    /// </summary>
    Task InvalidateCacheAsync(string tenantId, string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached folder mappings for a specific account.
    /// Returns the folder path to folder ID mappings.
    /// </summary>
    Task<Dictionary<string, string>> GetAllFoldersAsync(string tenantId, string accountId, CancellationToken cancellationToken = default);
}
