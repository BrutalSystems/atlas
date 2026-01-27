using Atlas.Data;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Extensions;

public static class IHostBuilderExtensions
{
    public static IHostBuilder UseAutofac<TDbContext>(this IHostBuilder builder) where TDbContext : DbContext
    {
        return builder.ConfigureServices(services => services.AddAutofac())
            .ConfigureContainer<ContainerBuilder>(builder =>
            {
                builder.RegisterType<BaseDbContext>().PropertiesAutowired();
                builder.RegisterType<TDbContext>().PropertiesAutowired();

                // var assemblyPrefixes = new string[] { typeof(TDbContext).FullName.Split('.')[0] };
                // var types = new TypeScanner(assemblyPrefixes).FindSubclassOfInterface<ICrudService>();
                // foreach (var type in types)
                // {
                //     builder.RegisterType(type).PropertiesAutowired();
                // }

                // Register services with Autofac
                // builder.RegisterModule(new AutofacConfigurationModule()); // Assuming you have a module
            });
    }

}