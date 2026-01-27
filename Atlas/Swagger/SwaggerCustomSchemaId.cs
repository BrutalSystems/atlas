namespace Atlas.Swagger;

public static class SwaggerCustomSchemaId
{
    public static string GetSchemaId(Type type)
    {
        var attrib = type.GetCustomAttributes(typeof(SwaggerSchemaIdAttribute), false).FirstOrDefault() as SwaggerSchemaIdAttribute;
        if (attrib != null)
        {
            return attrib.SchemaId;
        }

        if (type.IsGenericType)
        {
            var typeNames = type.GetGenericArguments().Select(t => t.Name);
            var genericTypes = string.Join(", ", typeNames);
            return type.Name.Split('`')[0] + "<" + genericTypes + ">";
        }
        return type.Name;
    }
}