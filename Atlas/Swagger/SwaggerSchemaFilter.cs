using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atlas.Swagger;

public class SwaggerSchemaFilter : ISchemaFilter
{

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        var model = schema as OpenApiSchema;
        if (model == null) return;

        if (context.Type.IsEnum)
        {
            model.Type = JsonSchemaType.String;
            model.Format = null;
            model.Enum!.Clear();
            foreach (string enumName in Enum.GetNames(context.Type))
            {
                MemberInfo? memberInfo = context.Type.GetMember(enumName).FirstOrDefault(m => m.DeclaringType == context.Type);
                EnumMemberAttribute? enumMemberAttribute = memberInfo == null
                    ? null
                    : memberInfo.GetCustomAttributes(typeof(EnumMemberAttribute), false).OfType<EnumMemberAttribute>().FirstOrDefault();
                var label = enumMemberAttribute == null || string.IsNullOrWhiteSpace(enumMemberAttribute.Value)
                    ? enumName
                    : enumMemberAttribute.Value;
                // In newer OpenAPI versions, just add the string value directly
                model.Enum.Add(label);
            }
        }
        else if (context.Type.IsClass && model.Properties?.Count > 0)
        {
            foreach (var prop in model.Properties.Where(p => p.Value.Type == JsonSchemaType.Array))
            {
                var typeProp = context.Type.GetProperties().FirstOrDefault(p => p.Name.ToLower() == prop.Key.ToLower());
                if (typeProp != null)
                {
                    var isNullable = Reflection.IsNullable(typeProp);
                    // Note: Nullable property is not directly settable in newer OpenAPI versions
                    // The schema generation handles nullability based on the C# type annotations
                }
            }

            // foreach (var prop in model.Properties)
            // {
            //     var typeProp = context.Type.GetProperties().FirstOrDefault(p => p.Name.ToLower() == prop.Key.ToLower());
            //     if (typeProp != null)
            //     {
            //         var formatAttrib = typeProp.GetCustomAttributes<SwaggerPropertyFormatAttribute>().FirstOrDefault();
            //         if (formatAttrib != null && prop.Value is OpenApiSchema propSchema)
            //         {
            //             // Note: Format is read-only in newer OpenAPI versions
            //             // Consider using schema customization at a different point
            //             //prop.Value.Description -- relative field size would be useful for the frontend
            //         }
            //     }
            // }

            if (model.Properties?.ContainsKey("query") == true)
            {
                model.Properties.Remove("query");
            }

            // foreach (var prop in model.Properties)
            // {
            //     var typeProp = context.Type.GetProperties().FirstOrDefault(p => p.Name.ToLower() == "query");
            //     if (typeProp != null)
            //     {
            //         prop.Value.
            //     }
            // }  
        }
    }


}

public class Reflection
{
    // https://stackoverflow.com/questions/58453972/how-to-use-net-reflection-to-check-for-nullable-reference-type

    public static bool IsNullable(PropertyInfo property) => IsNullableHelper(property.PropertyType, property.DeclaringType, property.CustomAttributes);

    public static bool IsNullable(FieldInfo field) => IsNullableHelper(field.FieldType, field.DeclaringType, field.CustomAttributes);

    public static bool IsNullable(ParameterInfo parameter) => IsNullableHelper(parameter.ParameterType, parameter.Member, parameter.CustomAttributes);

    private static bool IsNullableHelper(Type memberType, MemberInfo? declaringType, IEnumerable<CustomAttributeData> customAttributes)
    {
        if (memberType.IsValueType)
            return Nullable.GetUnderlyingType(memberType) != null;

        var nullable = customAttributes
            .FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
        if (nullable != null && nullable.ConstructorArguments.Count == 1)
        {
            var attributeArgument = nullable.ConstructorArguments[0];
            if (attributeArgument.ArgumentType == typeof(byte[]))
            {
                var args = (ReadOnlyCollection<CustomAttributeTypedArgument>)attributeArgument.Value!;
                if (args.Count > 0 && args[0].ArgumentType == typeof(byte))
                {
                    return (byte)args[0].Value! == 2;
                }
            }
            else if (attributeArgument.ArgumentType == typeof(byte))
            {
                return (byte)attributeArgument.Value! == 2;
            }
        }

        for (var type = declaringType; type != null; type = type.DeclaringType)
        {
            var context = type.CustomAttributes
                .FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
            if (context != null &&
                context.ConstructorArguments.Count == 1 &&
                context.ConstructorArguments[0].ArgumentType == typeof(byte))
            {
                return (byte)context.ConstructorArguments[0].Value! == 2;
            }
        }

        // Couldn't find a suitable attribute
        return false;
    }
}