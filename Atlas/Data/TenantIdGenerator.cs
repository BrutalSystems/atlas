using Atlas.Mvc;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Atlas.Data;

public class TenantIdGenerator(UserContext userContext) : ValueGenerator<string?>
{
    public override string? Next(EntityEntry entry)
    {
        return userContext.TenantId;
    }

    public override bool GeneratesTemporaryValues => false; // Indicates that the generated values are permanent
}