using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class CompositeCrudControllerBase<TEntity> : ControllerBase where TEntity : class
{
    private readonly IGenericService<TEntity> _service;

    protected CompositeCrudControllerBase(IGenericService<TEntity> service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TEntity>>> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(result);
    }

    protected async Task<ActionResult<TEntity>> GetByKeys(params object[] keyValues)
    {
        var result = await _service.GetByIdAsync(keyValues);
        return result is null ? NotFound() : Ok(result);
    }

    protected async Task<ActionResult<TEntity>> CreateEntity(TEntity entity)
    {
        var created = await _service.CreateAsync(entity);
        return Ok(created);
    }

    protected async Task<IActionResult> UpdateEntity(TEntity entity, params object[] keyValues)
    {
        var updated = await _service.UpdateAsync(entity, keyValues);
        return updated ? NoContent() : NotFound();
    }

    protected async Task<IActionResult> DeleteByKeys(params object[] keyValues)
    {
        var deleted = await _service.DeleteAsync(keyValues);
        return deleted ? NoContent() : NotFound();
    }
}
