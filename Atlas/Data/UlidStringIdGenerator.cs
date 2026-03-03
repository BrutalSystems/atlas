using ByteAether.Ulid;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Atlas.Data;

public class UlidStringIdGenerator : ValueGenerator<string>
{
    public override string Next(EntityEntry entry)
    {
        return Ulid.New().ToString();
    }

    public override bool GeneratesTemporaryValues => false; // Indicates that the generated values are permanent
}