namespace Atlas.Models;

/// <summary>
/// Interface for entities that support audit tracking with creation and update timestamps.
/// Used by the BaseService for automatic audit field management.
/// </summary>
public interface IAuditable
{
    /// <summary>
    /// When the entity was created.
    /// </summary>
    DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// When the entity was last updated.
    /// </summary>
    DateTimeOffset? UpdatedAt { get; set; }
}