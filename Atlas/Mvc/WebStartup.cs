using System.Reflection;
using Atlas.Data;
using Atlas.Extensions;
using Atlas.Helpers;
using Atlas.Settings;
using Atlas.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atlas.Mvc;

public class WebStartup<TCustomStartup> where TCustomStartup : ICustomStartup, new()
{
    private IConfiguration Configuration { get; }
    protected ILogger Logger { get; set; }
    private TCustomStartup CustomStartup { get; }

    public WebStartup(IConfiguration configuration)
    {
        this.Configuration = configuration;
        this.Logger = Logging.CreateLogger<TCustomStartup>(this.Configuration);
        this.CustomStartup = new TCustomStartup();
    }

    public void ConfigureServices(IServiceCollection services)
    {
        Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = Env.IsDevelopment;

        // for postgres (date handling - if i recall correctly)
        System.AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true); 

        services.AddControllers(o =>
            {
                o.EnableEndpointRouting = false;
                var routePrefix = $"/{typeof(TCustomStartup).Assembly.FullName!.Split(".")[0].ToLower()}/api";
                var routeTemplate = routePrefix;
                // o.UseCentralRoutePrefix(new Microsoft.AspNetCore.Mvc.RouteAttribute(routeTemplate), "[controller]");
                o.UseEnabledActions();
            })
            .AddApplicationPart(typeof(TCustomStartup).Assembly)
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ReferenceLoopHandling =
                    Newtonsoft.Json.ReferenceLoopHandling
                        .Ignore; // w/o this we'll get cyclic errors... User->UserRoles->User 
                options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented; // great for debugging
                options.SerializerSettings.Converters.Add(
                    new StringEnumConverter()); // string enum support; see StockCountStatus in swagger
                options.SerializerSettings.DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            });
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(s =>
        {
            this.CustomStartup.ConfigureSwagger(s);
            s.SchemaFilter<SwaggerSchemaFilter>(); // string enum code gen support; see StockCountStatus in swagger
            // s.ParameterFilter<SwaggerParamFilter>();
            s.OperationFilter<SwaggerOperationFilter>();
            s.CustomOperationIds(apiDesc =>
            {
                var opId = apiDesc.TryGetMethodInfo(out MethodInfo mi) ? mi.Name : null;
                return opId; // we want the operationId to be the methodName NOT the http(Get), Put, etc.
            });
            s.CustomSchemaIds(SwaggerCustomSchemaId.GetSchemaId);
        });

        var authSettings = this.Configuration.GetSettings<AuthSettings>();
        if (authSettings.UseMockAuthentication || Env.IsTest)
        {
            // this is a custom mock auth provider that mimics an authenticated user -- for Dev
            services.AddAuthentication(MockAuthenticationHandler.AuthenticationScheme).AddScheme<MockAuthenticationOptions, MockAuthenticationHandler>(MockAuthenticationHandler.AuthenticationScheme, option => { });
        }
        else
        {
            services.AddAuthentication()
                .AddJwtBearer("Bearer", options =>
                {
                    options.Authority = authSettings.Authority;
                    options.MetadataAddress = $"{authSettings.Authority}/.well-known/openid-configuration";
                    options.Audience = authSettings.Audience;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                    {
                        ValidateIssuer = authSettings.ValidateIssuer
                    };
                    options.Events = new JwtBearerEvents()
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];

                            // If the request is for our hub...
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken) && path.ToString().ToLower().EndsWith("/api/hub"))
                            {
                                // Read the token out of the query string
                                context.Token = accessToken;
                            }

                            return Task.CompletedTask;
                        }
                    };
                });
        }

        services.AddAuthorization();

        services.AddCors(options => options.AddPolicy("_defaultCorsPolicy", policy =>
        {
            policy
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetIsOriginAllowed(origin => true) // allow any origin; todo: may need to turn off at some point
                .AllowCredentials();
        }));

        services.AddSingleton(authSettings);
        services.AddHttpContextAccessor();

        this.CustomStartup.ConfigureServices(services, this.Configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        this.CustomStartup.PreConfigure(app, env);

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            if (true)
            {
                app.UseSwagger();
                var title = typeof(TCustomStartup).Assembly.FullName!.Split(".")[0];
                // app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{title}.Api v1"));
            }
        }

        if (app is WebApplication webApp)
        {
            app.UseCors("_defaultCorsPolicy"); // <-- needs to be between routing and useAuthentication (https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-9.0)
            app.UseAuthorization();
            webApp
                .MapControllers() // we are using Controllers, rather than minimal Api
                .RequireAuthorization(); // need this so that controller calls get an (Get)Endpoint in the MockAuthenticationHandler  
            this.CustomStartup.MapEndpoints(webApp);
        }
        else
        {
            // support for IWebHostBuilder -- used by Microsoft.AspNetCore.TestHost.
            app.UseRouting();
            app.UseCors("_defaultCorsPolicy"); // <-- needs to be between routing and useAuthentication (https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-9.0)
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers()
                    .WithMetadata(
                        new AuthorizeAttribute() { AuthenticationSchemes = MockAuthenticationHandler.AuthenticationScheme }
                    )
                    .RequireAuthorization();
                this.CustomStartup.MapEndpoints(endpoints);
            });
        }

        this.CustomStartup.Configure(app, env);
    }
}