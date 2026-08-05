namespace Atlas.Data;

[AttributeUsage(AttributeTargets.Class)]
public class SchemaAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public static string? GetSchemaName(Type dbContextType) =>
        (dbContextType.GetCustomAttributes(typeof(SchemaAttribute), inherit: true)
            .FirstOrDefault() as SchemaAttribute)?.Name;
}
