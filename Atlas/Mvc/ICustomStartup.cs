using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atlas.Mvc;

public interface ICustomStartup
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void PreConfigure(IApplicationBuilder app, IWebHostEnvironment env);
    void Configure(IApplicationBuilder app, IWebHostEnvironment env);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    void ConfigureSwagger(SwaggerGenOptions options);
}