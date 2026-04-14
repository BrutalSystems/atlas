using System.Text;
using Newtonsoft.Json.Linq;

namespace Atlas.Mvc;

public class RequestCleanerMiddleware(RequestDelegate next)
{
    private static Dictionary<string, List<string>> _propertiesToRemove = new()
    {
        { "/api/User", new List<string> { "UserRoles", "UserTenants",  } }
    };

    public static void AddTarget(string path, List<string> properties)
    {
        _propertiesToRemove[path] = properties;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Check if the current path matches any configured paths
            var matchingEntry = _propertiesToRemove.FirstOrDefault(kvp =>
                context.Request.Path.StartsWithSegments(kvp.Key));

            if (matchingEntry.Key != null &&
                (context.Request.Method == "PUT" || context.Request.Method == "POST"))
            {
                context.Request.EnableBuffering();

                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrEmpty(body))
                {
                    var json = JObject.Parse(body);
                    // Console.WriteLine("Original Request Body: " + json.ToString());

                    var removedProperties = new List<string>();

                    // Remove properties based on the dictionary configuration
                    foreach (var propertyName in matchingEntry.Value)
                    {
                        // Try both PascalCase and camelCase
                        if (json.Remove(propertyName))
                            removedProperties.Add(propertyName);

                        var camelCase = char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
                        if (json.Remove(camelCase))
                            removedProperties.Add(camelCase);
                    }

                    if (removedProperties.Any())
                    {
                        Console.WriteLine("Modified Request Body: " + json.ToString());

                        // Add custom headers to indicate modification
                        context.Response.Headers.Append("X-Request-Modified", "true");
                        context.Response.Headers.Append("X-Removed-Properties", string.Join(",", removedProperties));

                        var modifiedBody = json.ToString();
                        var bytes = Encoding.UTF8.GetBytes(modifiedBody);
                        context.Request.Body = new MemoryStream(bytes);
                        context.Request.ContentLength = bytes.Length;
                    }
                }
            }
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            // ignore
        }
        catch (Exception)
        {
            throw;
        }

        await next(context);
    }
}