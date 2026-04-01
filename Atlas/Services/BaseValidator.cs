using Atlas.Models;

namespace Atlas.Services;

public abstract class BaseValidator<TEntity> where TEntity : BaseModel
{
    public abstract Task<List<string>> ValidateAsync(TEntity entity, bool isNew, CancellationToken cancellationToken = default);
}