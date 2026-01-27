namespace Atlas.Models;

/// <summary>
/// Interface for entities that are scoped to a specific tenant.
/// Used by the BaseService for automatic tenant filtering and validation.
/// </summary>
public interface ITenantScoped
{
    /// <summary>
    /// The ID of the tenant this entity belongs to.
    /// </summary>
    string? TenantId { get; set; }
}