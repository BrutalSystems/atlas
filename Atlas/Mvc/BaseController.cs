using Atlas.Data;
using Atlas.Helpers;
using Atlas.Models;
using Atlas.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Mvc;

public class BaseController : ControllerBase
{

}

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<TEntity, TContext>(BaseService<TEntity, TContext> service) : BaseController
    where TEntity : BaseModel
    where TContext : BaseDbContext
{
    protected readonly BaseService<TEntity, TContext> ModelService = service ?? throw new ArgumentNullException(nameof(service));
    public ILogger<BaseController<TEntity, TContext>> Logger { get; set; } = NullLogger<BaseController<TEntity, TContext>>.Instance;

    /// <summary>
    /// Get all entities
    /// </summary>
    [HttpGet]
    public virtual async Task<ActionResult<IEnumerable<TEntity>>> ReadAll(CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await ModelService.ReadAllAsync(cancellationToken);
            return Ok(entities);
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving all {EntityType}", typeof(TEntity).Name);
            return StatusCode(500, new { Message = "An error occurred while retrieving entities" });
        }
    }

    /// <summary>
    /// Get entity by ID
    /// </summary>
    [HttpGet("{id}")]
    public virtual async Task<ActionResult<TEntity>> ReadOne(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await ModelService.ReadOneAsync(id, cancellationToken);

            if (entity == null)
            {
                return NotFound(new { Message = $"{typeof(TEntity).Name} with ID {id} not found" });
            }

            return Ok(entity);
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            return StatusCode(500, new { Message = "An error occurred while retrieving the entity" });
        }
    }

    /// <summary>
    /// Advanced query endpoint supporting filtering, sorting, and pagination
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ActionResult<ReadResponse<TEntity>>> Read([FromBody] ReadRequest? request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ModelService.ReadAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error querying {EntityType}", typeof(TEntity).Name);
            return StatusCode(500, new { Message = "An error occurred while querying entities" });
        }
    }

    /// <summary>
    /// Create a new entity
    /// </summary>
    [HttpPost]
    public virtual async Task<ActionResult<TEntity>> Create([FromBody] TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            if (entity == null)
            {
                return BadRequest(new { Message = "Entity cannot be null" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var created = await ModelService.CreateAsync(entity, cancellationToken);

            // Try to get the ID property value for the CreatedAtAction
            var idProperty = typeof(TEntity).GetProperty("Id");
            var idValue = idProperty?.GetValue(created)?.ToString();

            if (!string.IsNullOrEmpty(idValue))
            {
                return CreatedAtAction(nameof(ReadOne), new { id = idValue }, created);
            }

            return Ok(created);
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating {EntityType}", typeof(TEntity).Name);
            return StatusCode(500, new { Message = $"An error occurred while creating the entity; {ex.InnerException?.Message}" });
        }
    }

    /// <summary>
    /// Update an existing entity
    /// </summary>
    [HttpPut("{id}")]
    public virtual async Task<ActionResult<TEntity>> Update(string id, [FromBody] TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            if (entity == null)
            {
                return BadRequest(new { Message = "Entity cannot be null" });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Verify the entity exists
            var existing = await ModelService.ReadOneAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { Message = $"{typeof(TEntity).Name} with ID {id} not found" });
            }

            // Set the ID on the entity if it has an Id property
            // todo: we have a better way with BaseModel
            var idProperty = typeof(TEntity).GetProperty("Id");
            if (idProperty != null && idProperty.CanWrite)
            {
                idProperty.SetValue(entity, id);
            }

            try
            {
                var updated = await ModelService.UpdateAsync(entity, cancellationToken);
                return Ok(updated);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                return Unauthorized(new { Message = uaEx.Message });
            }
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            return StatusCode(500, new { Message = "An error occurred while updating the entity" });
        }
    }

    /// <summary>
    /// Delete an entity by ID
    /// </summary>
    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await ModelService.DeleteAsync(id, cancellationToken);

            if (!deleted)
            {
                return NotFound(new { Message = $"{typeof(TEntity).Name} with ID {id} not found" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException uaEx)
        {
            return Unauthorized(new { Message = uaEx.Message });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            return StatusCode(500, new { Message = "An error occurred while deleting the entity" });
        }
    }
}

public class BaseController<TEntity, TContext, TService>(TService service) : BaseController<TEntity, TContext>(service)
    where TEntity : BaseModel
    where TService : BaseService<TEntity, TContext>
    where TContext : BaseDbContext
{
    protected new readonly TService ModelService = service ?? throw new ArgumentNullException(nameof(service));
}