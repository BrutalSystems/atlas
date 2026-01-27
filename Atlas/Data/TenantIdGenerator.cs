using Atlas.Auth;
using ByteAether.Ulid;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

public class TenantIdGenerator(UserContext userContext) : ValueGenerator<string?>
{
    public override string? Next(EntityEntry entry)
    {
        return userContext.TenantId;
    }

    public override bool GeneratesTemporaryValues => false; // Indicates that the generated values are permanent
}