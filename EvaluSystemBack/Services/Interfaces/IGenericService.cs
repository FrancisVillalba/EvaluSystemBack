namespace EvaluSystemBack.Services.Interfaces;

public interface IGenericService<TEntity> where TEntity : class
{
    Task<IEnumerable<TEntity>> GetAllAsync();
    Task<TEntity?> GetByIdAsync(params object[] keyValues);
    Task<TEntity> CreateAsync(TEntity entity);
    Task<bool> UpdateAsync(TEntity entity, params object[] keyValues);
    Task<bool> DeleteAsync(params object[] keyValues);
}
