using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Atlas.Auth;
using Atlas.Helpers;
using Atlas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Data;

/// <summary>
/// Base class for DbContext implementations providing common functionality.
/// </summary>
public class BaseDbContext(DbContextOptions options, UserContext userContext) : DbContext(options)
{
    public static bool NullUserContextTenantIdAllowed { get; set; } = false;
    
    protected ILogger Logger { get; set; } = NullLogger<BaseDbContext>.Instance; //todo:  would be better if 
    public UserContext UserContext { get; set; } = userContext;

    // injected by Autofac... so we can create a logger with the right type
    public ILoggerFactory? LoggerFactory
    {
        set
        {
            if (value != null)
            {
                this.Logger = value.CreateLogger(GetType()) ?? NullLogger.Instance;
            }
        }
    }

    protected Dictionary<Type, ValueGenerator>? KeyValueGenerators { get; } = new()
    {
        { typeof(string), new UlidStringIdGenerator()}
    };

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        var isSqlite = base.Database.ProviderName != null && base.Database.ProviderName.Contains("Sqlite");
        if (isSqlite)
        {
            //*** Note: After making this change, you'll need to generate and apply a new migration to update the database schema. 
            configurationBuilder.Properties<string>().UseCollation("NOCASE");
        }

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // this.Logger.LogDebug("Configuring DbContext of type {DbContextType}", this.GetType().Name);

        var configuration = Env.GetConfiguration();
        DatabaseConfiguration.ConfigureOptionsBuilder(optionsBuilder, configuration, this.GetType());
        // Logger = Logging.CreateLogger(this.GetType().Name, configuration);

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureKeyValueGenerators(modelBuilder);
        ConfigureEntities(modelBuilder);
        ConfigureAuditProperties(modelBuilder);
        ConfigureTenantFilters(modelBuilder);
    }

    protected virtual void ConfigureKeyValueGenerators(ModelBuilder modelBuilder)
    {
        if (this.KeyValueGenerators != null)
        {
            foreach (var valueGeneratorType in this.KeyValueGenerators.Keys)
            {
                var valueGenerator = this.KeyValueGenerators[valueGeneratorType];
                modelBuilder.Model.GetEntityTypes().ToList().ForEach(et =>
                {
                    var pk = et.FindPrimaryKey();
                    if (pk == null) return;
                    pk.Properties.Where(p => p.ClrType == valueGeneratorType).ToList().ForEach(k =>
                    {
                        modelBuilder
                            .Entity(et.ClrType)
                            .Property(k.Name)
                            .HasValueGenerator((p, tb) => valueGenerator);
                    });
                });
            }
        }
    }

    /// <summary>
    /// Override this method to configure entity relationships and special cases.
    /// </summary>
    protected virtual void ConfigureEntities(ModelBuilder modelBuilder)
    {
        // Override in derived classes to configure specific entity behaviors
    }

    /// <summary>
    /// Configures common timestamp properties for entities that implement audit interfaces.
    /// </summary>
    protected virtual void ConfigureAuditProperties(ModelBuilder modelBuilder)
    {
        // Set default values for CreatedAt and UpdatedAt if your entities have these properties
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.GetProperty("CreatedAt") != null)
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTimeOffset?>("CreatedAt")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
            }

            if (entityType.ClrType.GetProperty("UpdatedAt") != null)
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTimeOffset?>("UpdatedAt")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
            }
        }
    }

    private void ConfigureTenantFilters(ModelBuilder modelBuilder)
    {
        // var tenantId = this.UserContext?.TenantId;
        
        // if (this.UserContext?.TenantId == null)
        // {
        //     Logger.LogWarning("UserContext is null during OnModelCreating; tenant filters will not be applied.");
        //     return;  // No user context available, skip tenant scoping
        // }

        // var tenantFilterMethod = this.GetType().GetMethod(nameof(TenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;


        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                // var filter = tenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, null);
                // var entityTypeBuilder = modelBuilder.Entity(entityType.ClrType);
                // entityTypeBuilder = entityTypeBuilder.HasQueryFilter(filter as LambdaExpression);

                // this does not work...  no way of handling UserContext?.TenantId == null
                var method = typeof(BaseDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType.ClrType);
                method?.Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private string? GetTenantId()
    {
        return this.UserContext.TenantId;
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantScoped
    {
        // tenantId is only captured at setup time, not per-query
        var tenantId = this.UserContext.TenantId;
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            (GetTenantId() == null) || e.TenantId == GetTenantId());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.PreSaveChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        this.PreSaveChanges();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected virtual void PreSaveChanges()
    {
        if (this.UserContext == null)
        {
            (this.Logger ?? NullLogger<BaseDbContext>.Instance)
                .LogWarning("UserContext is null during OnModelCreating; tenant filters will not be applied.");
            return;  // No user context available, skip tenant scoping
        }

        var savedOn = DateTimeOffset.UtcNow;    //note: api server times need to be correct
        // var nextConcurrency = Guid.NewGuid();
        var handled = new List<EntityEntry>();
        while (true)
        {
            var changed = ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged && !handled.Any(h => h.Entity == e.Entity)).ToList();
            if (changed.Count == 0)
                break;

            foreach (var entry in changed)
            {
                //todo: might need a way for an SYSTEM user to bypass and set a specific TENANT
                if (entry.Entity is ITenantScoped tenantScoped)
                {
                    if (this.UserContext.TenantId == null)
                    {
                        if (tenantScoped.TenantId != null)
                        {
                            // this is ok
                        }
                        else
                        {
                            throw new InvalidOperationException($"TenantId is required for tenant-scoped entities ({entry.Entity.GetType().Name}).");
                        }
                    }
                    else
                    {
                        tenantScoped.TenantId = this.UserContext.TenantId!;
                    }
                }

                //todo:  do we want a way for a SYSTEM user to override (maybe for an import)
                if (entry.Entity is IAuditable auditable)
                {
                    if (entry.State == EntityState.Added)
                        auditable.CreatedAt = savedOn;
                    auditable.UpdatedAt = savedOn;
                }
            }

            handled.AddRange(changed);
        }
    }

    public bool IsSqlite()
    {
        var isSqlite = base.Database.ProviderName != null && base.Database.ProviderName.Contains("Sqlite");
        return isSqlite;
    }
}
