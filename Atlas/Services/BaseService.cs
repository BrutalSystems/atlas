using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Atlas.Models;
using Atlas.Extensions;
using Atlas.Data;
using Atlas.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Services;

public class BaseService
{

}

/// <summary>
/// Base service class providing common CRUD operations for entities.
/// Includes automatic tenant isolation for entities that implement ITenantScoped.
/// </summary>
/// <typeparam name="TEntity">The entity type this service manages</typeparam>
/// <typeparam name="TContext">The DbContext type</typeparam>
public class BaseService<TEntity, TContext> : BaseService where TEntity : BaseModel where TContext : BaseDbContext
{
    protected readonly TContext _context;
    protected readonly DbSet<TEntity> _dbSet;
    public virtual IQueryable<TEntity> Query => _dbSet.AsQueryable();
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

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

    public BaseService(TContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<TEntity>();
    }

    public TContext Context => _context;

    // ----- Core CRUD Operations

    /// <summary>
    /// Advanced query method that supports filtering, sorting, and pagination via ReadRequest.
    /// </summary>
    /// <param name="request">ReadRequest with filters, sorts, skip/take parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ReadResponse with matching entities and total count</returns>
    public virtual async Task<ReadResponse<TEntity>> ReadAsync(ReadRequest? request, CancellationToken cancellationToken = default)
    {
        var query = (request?.Query as IQueryable<TEntity>) ?? this.Query ?? _dbSet.AsQueryable();

        // Apply filters
        if (request?.Filters != null)
        {
            foreach (var filter in request.Filters)
            {
                if (filter.Operation == null)
                    continue;
                query = query.Where(GetFilter<TEntity>(filter));
            }
        }

        // Apply sorting
        if (request?.Sorts != null)
        {
            var first = true;
            foreach (var sort in request.Sorts)
            {
                if (string.IsNullOrEmpty(sort.ColId))
                    continue;

                if (sort.Sort == "asc" && first)
                    query = query.OrderByColumn(sort.ColId, this.Context.IsSqlite());
                else if (sort.Sort == "asc" && !first)
                    query = (query as IOrderedQueryable<TEntity>)!.ThenByColumn(sort.ColId, this.Context.IsSqlite());
                else if (sort.Sort == "desc" && first)
                    query = query.OrderByColumnDescending(sort.ColId, this.Context.IsSqlite());
                else if (sort.Sort == "desc" && !first)
                    query = (query as IOrderedQueryable<TEntity>)!.ThenByColumnDescending(sort.ColId, this.Context.IsSqlite());
                first = false;
            }
        }

        try
        {
            var total = await query.CountAsync(cancellationToken);
            var dataQuery = query;
            if (request?.Skip != null)
            {
                dataQuery = dataQuery.Skip(request.Skip.Value);
            }
            if (request?.Take != null)
            {
                dataQuery = dataQuery.Take(request.Take.GetValueOrDefault());
            }
            var data = await dataQuery.ToListAsync(cancellationToken);
            
            return new ReadResponse<TEntity> { Data = data, LastRow = total };
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Error executing query: {Message}", ex.Message);
            throw new InvalidOperationException($"Error executing query: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets an entity by its ID.
    /// </summary>
    /// <param name="id">The entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The entity if found, null otherwise</returns>
    public virtual async Task<TEntity?> ReadOneAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var res = await this.Query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        await AccessCheck(res); // doing this even if res is null to prevent info leak (giving a notfound for an id the user has no access to)
        return res;
    }

    /// <summary>
    /// Gets all entities. For tenant-scoped entities, automatically filters by current tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entities</returns>
    public virtual async Task<List<TEntity>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await AccessCheck();

        return await _dbSet.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a new entity.
    /// </summary>
    /// <param name="entity">The entity to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created entity</returns>
    public virtual async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await AccessCheck(entity);

        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        _dbSet.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    /// <param name="entity">The entity to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated entity</returns>
    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await this.AccessCheck(entity);

        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        // Check if entity is already being tracked
        var trackedEntity = _context.ChangeTracker.Entries<TEntity>().FirstOrDefault(e => e.Entity.Id == entity.Id);

        if (trackedEntity != null)
        {
            // Entity is already tracked, update the tracked entity's properties
            _context.Entry(trackedEntity.Entity).CurrentValues.SetValues(entity);
        }
        else
        {
            // Entity is not tracked, attach and mark as modified
            _dbSet.Update(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<TEntity> UpsertAsync(TEntity entity, Expression<Func<TEntity, bool>>? match = null, CancellationToken cancellationToken = default)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        if (match != null)
        {
            var foundEntity = await FirstOrDefaultAsync(match, cancellationToken);
            if (foundEntity != null)
            {
                entity.Id = foundEntity.Id;
                return await UpdateAsync(entity, cancellationToken);
            }
        }

        var id = entity.Id;
        if (id == null)
        {
            return await CreateAsync(entity, cancellationToken);
        }

        var existingEntity = await ReadOneAsync(id, cancellationToken);
        if (existingEntity != null)
        {
            return await UpdateAsync(entity, cancellationToken);
        }
        else
        {
            return await CreateAsync(entity, cancellationToken);
        }
    }

    /// <summary>
    /// Upserts an entity by matching on specified property names.
    /// </summary>
    /// <param name="entity">The entity to upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="propertyNames">Property names to use for matching</param>
    /// <returns>The upserted entity</returns>
    public virtual async Task<TEntity> UpsertAsync(TEntity entity, params string[] propertyNames)
    {
        CancellationToken cancellationToken = default;

        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        if (propertyNames == null || propertyNames.Length == 0)
        {
            // No property names specified, fall back to default behavior
            return await UpsertAsync(entity, null, cancellationToken);
        }

        // Build dynamic expression: e => e.Prop1 == entity.Prop1 && e.Prop2 == entity.Prop2 ...
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? combinedExpression = null;

        foreach (var propName in propertyNames)
        {
            var property = typeof(TEntity).GetProperty(propName);
            if (property == null)
                throw new ArgumentException($"Property '{propName}' does not exist on type '{typeof(TEntity).Name}'");

            var propertyAccess = Expression.Property(parameter, property);
            var propertyValue = property.GetValue(entity);
            var constantValue = Expression.Constant(propertyValue, property.PropertyType);
            var equalityExpression = Expression.Equal(propertyAccess, constantValue);

            combinedExpression = combinedExpression == null ? equalityExpression : Expression.AndAlso(combinedExpression, equalityExpression);
        }

        if (combinedExpression == null)
            return await UpsertAsync(entity, null, cancellationToken);

        var lambda = Expression.Lambda<Func<TEntity, bool>>(combinedExpression, parameter);
        return await UpsertAsync(entity, lambda, cancellationToken);
    }

    /// <summary>
    /// Deletes an entity by ID.
    /// </summary>
    /// <param name="id">The entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    public virtual async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        var entity = await ReadOneAsync(id, cancellationToken);
        if (entity == null)
            return false;

        return await DeleteAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Deletes an entity.
    /// </summary>
    /// <param name="entity">The entity to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false otherwise</returns>
    public virtual async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await AccessCheck(entity);

        if (entity == null)
            return false;

        _dbSet.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ----- Query Operations

    /// <summary>
    /// Finds entities matching the specified predicate.
    /// </summary>
    /// <param name="predicate">The search predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching entities</returns>
    public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return await this.Query.Where(predicate).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the first entity matching the predicate, or null if none found.
    /// </summary>
    /// <param name="predicate">The search predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>First matching entity or null</returns>
    protected virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return await this.Query.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Checks if an entity with the specified ID exists.
    /// </summary>
    /// <param name="id">The entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if exists, false otherwise</returns>
    protected virtual async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return await ReadOneAsync(id, cancellationToken) != null;
    }

    /// <summary>
    /// Checks if any entity matches the specified predicate.
    /// </summary>
    /// <param name="predicate">The search predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if any entity matches, false otherwise</returns>
    protected virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Counts all entities. For tenant-scoped entities, automatically filters by current tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity count</returns>
    protected virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Counts entities matching the specified predicate.
    /// </summary>
    /// <param name="predicate">The search predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Count of matching entities</returns>
    protected virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return await _dbSet.CountAsync(predicate, cancellationToken);
    }

    // ----- Private Filter Helper Methods

    private Expression<Func<T, bool>> GetFilter<T>(ReadFilter filter)
    {
        var parameter = Expression.Parameter(typeof(T));
        var filterExpression = GetFilterExpressions<T>(filter, parameter);
        return Expression.Lambda<Func<T, bool>>(filterExpression, parameter);
    }

    private Expression GetFilterExpressions<T>(ReadFilter filter, ParameterExpression parameter)
    {
        if (string.IsNullOrEmpty(filter.PropertyName))
            throw new Atlas.Models.InvalidFilterCriteriaException("PropertyName is required for filter");

        // Build member access expression (supports nested properties)
        Expression member = parameter;
        foreach (var namePart in filter.PropertyName.Split('.'))
        {
            member = Expression.Property(member, namePart);
        }

        var targetType = member.Type;
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            targetType = Nullable.GetUnderlyingType(targetType);

        if (targetType == null)
            throw new InvalidOperationException($"Property '{filter.PropertyName}' does not exist on type '{typeof(T).Name}'.");

        Expression filterExpression;

        // Handle logical operators (And/Or) with conditions
        switch (filter.Operation)
        {
            case ReadFilterOperator.And:
                if (filter.Conditions == null || filter.Conditions.Count == 0)
                    throw new Atlas.Models.InvalidFilterCriteriaException("And operation requires conditions");

                filterExpression = GetFilterExpressions<T>(filter.Conditions[0], parameter);
                for (int i = 1; i < filter.Conditions.Count; i++)
                {
                    var nextFilterExpression = GetFilterExpressions<T>(filter.Conditions[i], parameter);
                    filterExpression = Expression.And(filterExpression, nextFilterExpression);
                }

                break;

            case ReadFilterOperator.Or:
                if (filter.Conditions == null || filter.Conditions.Count == 0)
                    throw new Atlas.Models.InvalidFilterCriteriaException("Or operation requires conditions");

                filterExpression = GetFilterExpressions<T>(filter.Conditions[0], parameter);
                for (int i = 1; i < filter.Conditions.Count; i++)
                {
                    var nextFilterExpression = GetFilterExpressions<T>(filter.Conditions[i], parameter);
                    filterExpression = Expression.Or(filterExpression, nextFilterExpression);
                }

                break;

            case ReadFilterOperator.Blank:
            case ReadFilterOperator.NotBlank:
                filterExpression = BuildNullOrEmptyExpression(member);
                if (filter.Operation == ReadFilterOperator.NotBlank)
                    filterExpression = Expression.Not(filterExpression);
                break;

            default:
                // All other operations require a value
                if (string.IsNullOrEmpty(filter.Value))
                    throw new Atlas.Models.InvalidFilterCriteriaException($"Operation {filter.Operation} requires a value");

                var typedValue = TypeDescriptor.GetConverter(targetType).ConvertFromInvariantString(filter.Value);
                var isDateTime = typedValue is DateTime || typedValue is DateTime?;
                var isDateTimeOffset = typedValue is DateTimeOffset || typedValue is DateTimeOffset?;

                if (isDateTime && filter.Value != null && filter.Value.EndsWith("Z"))
                {
                    // Convert to UTC if value ends with 'Z'
                    typedValue = (typedValue as DateTime?).GetValueOrDefault().ToUniversalTime();
                }
                else if (isDateTimeOffset && filter.Value != null)
                {
                    // For DateTimeOffset, parse with proper timezone handling
                    if (DateTimeOffset.TryParse(filter.Value, out var parsedDateTimeOffset))
                    {
                        typedValue = parsedDateTimeOffset;
                    }
                }

                var constExpression = Expression.Constant(typedValue, member.Type);

                filterExpression = filter.Operation switch
                {
                    ReadFilterOperator.Equals => Expression.Equal(member, constExpression),
                    ReadFilterOperator.NotEqual => Expression.NotEqual(member, constExpression),
                    ReadFilterOperator.Gt or ReadFilterOperator.GreaterThan => Expression.GreaterThan(member, constExpression),
                    ReadFilterOperator.GtOrEq => Expression.GreaterThanOrEqual(member, constExpression),
                    ReadFilterOperator.Lt or ReadFilterOperator.LessThan => Expression.LessThan(member, constExpression),
                    ReadFilterOperator.LtOrEq => Expression.LessThanOrEqual(member, constExpression),
                    ReadFilterOperator.Contains => BuildContainsExpression(member, filter.Value!, false),
                    ReadFilterOperator.NotContains => Expression.Not(BuildContainsExpression(member, filter.Value!, false)),
                    ReadFilterOperator.ContainsCaseSensitive => BuildContainsExpression(member, filter.Value!, true),
                    ReadFilterOperator.StartsWith => BuildStringMethodExpression(member, constExpression, nameof(string.StartsWith)),
                    ReadFilterOperator.EndsWith => BuildStringMethodExpression(member, constExpression, nameof(string.EndsWith)),
                    _ => throw new InvalidOperationException($"Unsupported filter operation: {filter.Operation}")
                };
                break;
        }

        return filterExpression;
    }

    private Expression BuildNullOrEmptyExpression(Expression member)
    {
        // Check if member is null
        var nullCheck = Expression.Equal(member, Expression.Constant(null, member.Type));

        // For string types, also check if empty
        if (member.Type == typeof(string))
        {
            var emptyCheck = Expression.Equal(member, Expression.Constant(string.Empty, typeof(string)));
            return Expression.OrElse(nullCheck, emptyCheck);
        }

        // For nullable types, just check null
        return nullCheck;
    }

    private Expression BuildContainsExpression(Expression member, string value, bool caseSensitive)
    {
        if (member.Type != typeof(string))
            throw new Atlas.Models.InvalidFilterCriteriaException("Contains operations are only supported on string properties");

        if (caseSensitive)
        {
            var containsMethodInfo = typeof(string).GetMethod(nameof(string.Contains), new Type[] { typeof(string) });
            var constExpression = Expression.Constant(value, typeof(string));
            return Expression.Call(member, containsMethodInfo!, constExpression);
        }
        else
        {
            // Use EF.Functions.Like for case-insensitive contains
            var efLikeMethod = typeof(DbFunctionsExtensions).GetMethod("Like", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(DbFunctions), typeof(string), typeof(string) }, null);

            if (efLikeMethod == null)
                throw new InvalidOperationException("EF.Functions.Like method not found");

            var likePattern = $"%{value}%";
            var patternExpression = Expression.Constant(likePattern, typeof(string));
            var efFunctionsProperty = Expression.Property(null, typeof(EF).GetProperty("Functions")!);

            return Expression.Call(null, efLikeMethod, efFunctionsProperty, member, patternExpression);
        }
    }

    private Expression BuildStringMethodExpression(Expression member, Expression valueExpression, string methodName)
    {
        if (member.Type != typeof(string))
            throw new Atlas.Models.InvalidFilterCriteriaException($"{methodName} operations are only supported on string properties");

        var methodInfo = typeof(string).GetMethod(methodName, new Type[] { typeof(string) });
        if (methodInfo == null)
            throw new InvalidOperationException($"String method {methodName} not found");

        return Expression.Call(member, methodInfo, valueExpression);
    }

    // ----- Pagination Support

    /// <summary>
    /// Gets a paginated list of entities.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated result</returns>
    protected virtual async Task<PagedResult<TEntity>> GetPagedAsync(int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 1000) pageSize = 1000; // Prevent excessive page sizes

        var totalCount = await CountAsync(cancellationToken);
        var skip = (pageNumber - 1) * pageSize;

        var items = await _dbSet.Skip(skip).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// Gets a paginated list of entities matching the specified predicate.
    /// </summary>
    /// <param name="predicate">Search predicate</param>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated result</returns>
    protected virtual async Task<PagedResult<TEntity>> GetPagedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 1000) pageSize = 1000;

        var query = _dbSet.Where(predicate);
        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (pageNumber - 1) * pageSize;

        var items = await query.Skip(skip).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, totalCount, pageNumber, pageSize);
    }

    // ----- I think these are not used anymore -- services get secured, not controllers

    protected bool? HasHeaderPermission(string permissionName, string? value = null, string? action = null)
    {
        var uc = this.Context.UserContext;
        if (uc == null || uc.AuthUserId.IsNullOrWhiteSpace())
            return null;

        var headers = this.Context.UserContext?.Headers;
        if (headers == null)
        {
            // todo: could check database directly if headers arent available     
            // but then i feel like we should always check in the service -- rather than at the API level
            // *** Services respect security only if the UserContext is available via an API request ***
            return null;
        }

        var foundValue = headers[$"Action-Permission:{permissionName}" + (value != null ? "=" + value : "")].FirstOrDefault();
        if (foundValue == null)
            return false; // shouldnt happen
        if (action == null)
            return foundValue != null;

        return foundValue.ToLowerInvariant() == action.ToLowerInvariant();
    }

    protected bool HasActionPermission(string action, string permissionName, string? value = null)
    {
        var permissions = GetRequiredPermissions(action);
        if (permissions.Count == 0)
            return true; // no permissions defined on service

        foreach (var ap in permissions)
        {
            if (ap.Permission.Equals(permissionName, StringComparison.OrdinalIgnoreCase))
            {
                if (ap.Value == null || ap.Value == value)
                {
                    return true;
                }
            }
            else if (ap.Permission == "*")
            {
                // wildcard to allow all actions
                return true;
            }
        }

        return false;
    }

    // ----- Access Control Methods

    protected List<ActionPermission> GetRequiredPermissions(string? action = null, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        action ??= callerName.EndsWith("Async") ? callerName[..^5] : callerName;

        var serviceType = this.GetType();
        var permissionAttributes = serviceType.GetCustomAttributes(typeof(ServicePermissionsAttribute), true).Cast<ServicePermissionsAttribute>();

        var allPermissions = new List<ActionPermission>();
        foreach (var attr in permissionAttributes)
        {
            allPermissions.AddRange(attr.Permissions.Where(p => action == "*" || p.Action.Equals(action, StringComparison.OrdinalIgnoreCase)));
        }

        return allPermissions;
    }

    protected async Task AccessCheck(TEntity? entity = null, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        var action = callerName.EndsWith("Async") ? callerName[..^5] : callerName;

        if (await UserHasNoAccess(action, entity))
            throw new UnauthorizedAccessException($"User does not have required permissions to perform action '{action}'.");
    }

    protected virtual async Task<bool> UserHasAccess(string actionName, TEntity? entity = null)
    {
        return true;
    }

    protected async Task<bool> UserHasNoAccess(string actionName, TEntity? entity = null)
    {
        return await UserHasAccess(actionName, entity) == false;
    }
}