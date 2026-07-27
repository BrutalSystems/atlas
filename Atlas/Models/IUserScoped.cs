namespace Atlas.Models;

/// <summary>
/// Interface for entities that are scoped to a specific authenticated user.
/// Used by BaseDbContext for automatic user filtering and AuthUserId stamping on insert.
/// </summary>
public interface IUserScoped
{
    /// <summary>
    /// The authentication provider's user ID (e.g. Firebase UID) this entity belongs to.
    /// </summary>
    string? UserId { get; set; }
}
