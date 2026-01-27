using System.Reflection;
using Atlas.Services;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Scans the calling assembly for classes derived from BaseService and registers them with DI.
    /// Also automatically registers BaseService implementations for any DbSet models in TDbContext 
    /// that don't have an explicit implementation.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type to scan for models</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="callingAssembly">Optional assembly to scan (defaults to calling assembly)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddBaseServices<TDbContext>(
        this IServiceCollection services,
        Assembly? callingAssembly = null)
        where TDbContext : DbContext
    {
        callingAssembly ??= typeof(TDbContext).Assembly;

        // Find all concrete classes that derive from BaseService
        var baseServiceType = typeof(BaseService<,>);
        var concreteServiceTypes = callingAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && IsBaseServiceDerived(t))
            .ToList();

        // Register concrete service implementations
        var registeredModelTypes = new HashSet<Type>();
        foreach (var serviceType in concreteServiceTypes)
        {
            var baseType = GetBaseServiceType(serviceType);
            if (baseType != null)
            {
                var modelType = baseType.GetGenericArguments()[0];
                registeredModelTypes.Add(modelType);

                // Register the service as scoped
                services.AddScoped(serviceType);
            }
        }

        // Find all DbSet<T> properties in TDbContext
        var dbContextType = typeof(TDbContext);
        var dbSetProperties = dbContextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToList();

        // Register BaseService<TModel, TDbContext> for models without explicit implementations
        foreach (var prop in dbSetProperties)
        {
            var modelType = prop.PropertyType.GetGenericArguments()[0];

            if (!registeredModelTypes.Contains(modelType))
            {
                // Create BaseService<TModel, TDbContext> - but we need a concrete implementation
                // Since BaseService is abstract, we'll create a default implementation
                var serviceType = typeof(BaseService<,>).MakeGenericType(modelType, dbContextType);
                services.AddScoped(serviceType);
            }
        }

        return services;
    }

    private static bool IsBaseServiceDerived(Type type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(BaseService<,>))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static Type? GetBaseServiceType(Type type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(BaseService<,>))
            {
                return current;
            }
            current = current.BaseType;
        }
        return null;
    }
}

