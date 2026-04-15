using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Email.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Atlas.Email services.
    /// The consuming application must separately register:
    ///   services.AddScoped&lt;IMailAccountStore, YourAccountStore&gt;()
    ///   services.AddScoped&lt;IFolderCacheService, YourFolderCacheService&gt;()
    /// </summary>
    public static IServiceCollection AddAtlasEmail(this IServiceCollection services)
    {
        services.AddHttpClient();
        return services;
    }
}
