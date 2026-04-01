using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Atlas.Models;

public class BaseModel<T> : IAuditable
{
    [Key]
    [MaxLength(36)]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public T? Id { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class BaseModel : BaseModel<string>;

public class BaseTenantModel : BaseModel, ITenantScoped
{
    [MaxLength(36)]
    public string? TenantId { get; set; }
}

