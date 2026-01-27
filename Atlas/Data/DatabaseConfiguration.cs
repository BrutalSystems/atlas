using System.Reflection;
using Atlas.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Data;

public static class DatabaseConfiguration
{
    public static string GetConnectionString(IConfiguration configuration, Type dbContextType)
    {
        var provider = configuration["DatabaseProvider"] ?? "SQLite";
        var name = $"{dbContextType.Name}-{provider}";

        var connectionString = configuration.GetConnectionString(name)
                               ?? throw new InvalidOperationException(
                                   $"{name} connection string is required for {provider} provider");

        return connectionString;
    }

    public static void ConfigureOptionsBuilder(DbContextOptionsBuilder options, IConfiguration configuration, Type dbContextType)
    {
        var provider = configuration["DatabaseProvider"] ?? "SQLite";
        var name = $"{dbContextType.Name}-{provider}";
        var codeName = dbContextType.Assembly.GetName().Name?.Split('.')[0];
        var startupAssembly = Assembly.GetEntryAssembly()?.GetName().Name;
        var isEfAddMigration = startupAssembly == "ef";

        // Console.WriteLine($"Configuring DbContextOptions for {startupAssembly} {dbContextType.Name} using provider '{provider}' with connection string name '{name}'");

        switch (provider.ToUpperInvariant())
        {
            case "POSTGRESQL":
            case "POSTGRES":
                var postgresConnectionString = isEfAddMigration ? "" : configuration.GetConnectionString(name)
                                               ?? throw new InvalidOperationException(
                                                   "PostgreSQLConnection string is required when using PostgreSQL provider");

                options.UseNpgsql(postgresConnectionString, npgsqlOptions =>
                {
                    // Enable retry on transient failures
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                });
                break;

            case "SQLITE":
                // Iterate through available connection strings
                // var connectionStringsSection = configuration.GetSection("ConnectionStrings");
                // Console.WriteLine("Available connection strings:");
                // foreach (var connString in connectionStringsSection.GetChildren())
                // {
                //     if (connString.Key == name)
                //         continue;

                //     Console.WriteLine($"  - {connString.Key}: {connString.Value}");
                // }

                var sqliteConnectionString = isEfAddMigration ? "" : configuration.GetConnectionString(name)
                                             ?? throw new InvalidOperationException(
                                                   "SQLiteConnection string is required when using SQLite provider");

                // Log the database path for debugging
                // Console.WriteLine($"Using SQLite database: {sqliteConnectionString}");

                options.UseSqlite(sqliteConnectionString, sqliteOptions =>
                {
                    // SQLite-specific options if needed
                });
                break;
            case "MSSQL":
                var mssqlConnectionString = isEfAddMigration ? "" : configuration.GetConnectionString(name)
                                             ?? throw new InvalidOperationException(
                                                   "MSSQLConnection string is required when using MSSQL provider");

                // Log the database path for debugging
                Console.WriteLine($"Using MSSQL database: {mssqlConnectionString}");

                options.UseSqlServer(mssqlConnectionString, sqlServerOptions =>
                {
                    var migrationAssemblyName = $"{codeName}.Migrations.SqlServer";
                    sqlServerOptions.MigrationsAssembly(migrationAssemblyName);
                });
                break;
        }

        // Common options for all providers
        // if (bool.TryParse(configuration["DatabaseLogging"], out var enableLogging) && enableLogging)
        // {
        //     options.EnableSensitiveDataLogging();
        //     // options.LogTo(Console.WriteLine);
        // }
    }

    /// <summary>
    /// Configures the database context based on the provider specified in configuration.
    /// Supports both SQLite (development/self-hosted) and PostgreSQL (production SaaS).
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to configure</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void ConfigureDatabase<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        var provider = configuration["DatabaseProvider"] ?? "SQLite";

        services.AddDbContext<TContext>(options => { ConfigureOptionsBuilder(options, configuration, typeof(TContext)); });
    }

    public static void ApplyMigrationsIfEnabled<TContext>(
        TContext context,
        IConfiguration configuration)
        where TContext : DbContext
    {
        var shouldAutoMigrate = bool.TryParse(configuration["AutoApplyMigrations"], out var autoMigrate) && autoMigrate;
        var loggerFactory = Logging.CreateLoggerFactory(configuration);

        if (shouldAutoMigrate)
        {
            try
            {
                var logger = loggerFactory?.CreateLogger("Atlas.Data.DatabaseConfiguration");

                logger?.LogInformation("Checking for pending database migrations...");

                logger?.LogInformation(context.Database.GetConnectionString());

                var pendingMigrations = context.Database.GetPendingMigrations().ToList();

                if (pendingMigrations.Any())
                {
                    logger?.LogInformation("Applying {Count} pending migrations: {Migrations}",
                        pendingMigrations.Count, string.Join(", ", pendingMigrations));

                    context.Database.Migrate();

                    logger?.LogInformation("Database migrations applied successfully");
                }
                else
                {
                    logger?.LogInformation("Database is up to date, no migrations needed");
                }
            }
            catch (Exception ex)
            {
                // var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger("Atlas.Data.DatabaseConfiguration");
                logger?.LogError(ex, "Failed to apply database migrations automatically");

                // In development, we might want to fail fast
                // In production, we might want to continue without auto-migration
                if (Env.IsDevelopment || System.Diagnostics.Debugger.IsAttached)
                {
                    throw new InvalidOperationException(
                        "Failed to apply database migrations. See inner exception for details.", ex);
                }
            }
        }
    }

    /// <summary>
    /// Applies database migrations automatically if configured to do so.
    /// This should be called after the application services are configured.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type</typeparam>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="environment">Host environment (optional, used for error handling)</param>
    public static void ApplyMigrationsIfEnabled<TContext>(IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        var configuration = Env.GetConfiguration();

        var shouldAutoMigrate = bool.TryParse(configuration["AutoApplyMigrations"], out var autoMigrate) && autoMigrate;

        if (shouldAutoMigrate)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                var loggerFactory = scope.ServiceProvider.GetService<ILoggerFactory>();
                ApplyMigrationsIfEnabled<TContext>(
                    context,
                    configuration);
            }
            catch (Exception ex)
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger("Atlas.Data.DatabaseConfiguration");
                logger?.LogError(ex, "Failed to apply database migrations automatically");

                // In development, we might want to fail fast
                // In production, we might want to continue without auto-migration
                if (Env.IsDevelopment)
                {
                    throw new InvalidOperationException(
                        "Failed to apply database migrations. See inner exception for details.", ex);
                }
            }
        }
    }

    /// <summary>
    /// Gets the current database provider being used.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The database provider name</returns>
    public static string GetDatabaseProvider(this IConfiguration configuration)
    {
        return configuration["DatabaseProvider"] ?? "SQLite";
    }

    /// <summary>
    /// Checks if the current provider is PostgreSQL.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>True if using PostgreSQL, false otherwise</returns>
    public static bool IsUsingPostgreSQL(this IConfiguration configuration)
    {
        var provider = configuration.GetDatabaseProvider();
        return provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the current provider is SQLite.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>True if using SQLite, false otherwise</returns>
    public static bool IsUsingSQLite(this IConfiguration configuration)
    {
        var provider = configuration.GetDatabaseProvider();
        return provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase);
    }
}