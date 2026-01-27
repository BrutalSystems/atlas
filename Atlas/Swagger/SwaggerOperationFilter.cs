using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atlas.Swagger;

public class SwaggerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var resp = operation.Responses;
        
        if (operation.Parameters != null) {
            // 'query' is used by the CrudService methods (and shouldn't be a parameter for the endpoint)
            var idx = operation.Parameters.ToList().FindIndex(e => e.Name?.ToLower() == "query");
            if (idx >= 0)
            {
                operation.Parameters.RemoveAt(idx);
            }
        }

        if (operation.Parameters != null) {
            // 'commit' is used by the CrudService methods (and shouldn't be a parameter for the endpoint)
            var idx = operation.Parameters.ToList().FindIndex(e => e.Name?.ToLower() == "commit");
            if (idx >= 0)
            {
                operation.Parameters.RemoveAt(idx);
            }
        }

        // foreach (var p in operation.Parameters)
        // {
        //     if (p.Name == "concurrency")
        //         p.Required = false;
        // }
    }
}