using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Atlas.Models;

public class BaseModel<T> : IAuditable
{
    [Key]
    [MaxLength(36)]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public T? Id { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class BaseModel : BaseModel<string>;

public class BaseTenantModel : BaseModel, ITenantScoped
{
    [MaxLength(36)]
    public string? TenantId { get; set; }
}

