namespace Atlas.Models;

/// <summary>
/// Interface for entities that support audit tracking with creation and update timestamps.
/// Used by the BaseService for automatic audit field management.
/// </summary>
public interface IAuditable
{
    string CreatedBy { get; set; }
    
    DateTimeOffset? CreatedAt { get; set; }

    string UpdatedBy { get; set; }
    
    DateTimeOffset? UpdatedAt { get; set; }
}