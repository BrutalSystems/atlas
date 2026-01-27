namespace Atlas.Swagger;

[AttributeUsage(AttributeTargets.Class)]
public class SwaggerSchemaIdAttribute(string schemaId) : Attribute
{
    public string SchemaId { get; } = schemaId;
}