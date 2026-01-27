namespace Atlas.Swagger;

[AttributeUsage(AttributeTargets.Property)]
public class SwaggerPropertyFormatAttribute(string format) : Attribute
{
    public string Format { get; } = format;
}