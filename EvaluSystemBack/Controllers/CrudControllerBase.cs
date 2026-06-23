using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class CrudControllerBase<TEntity, TKey> : ControllerBase where TEntity : class
{
    private readonly IGenericService<TEntity> _service;

    protected CrudControllerBase(IGenericService<TEntity> service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TEntity>>> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TEntity>> GetById(TKey id)
    {
        var result = await _service.GetByIdAsync(id!);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TEntity>> Create(TEntity entity)
    {
        var created = await _service.CreateAsync(entity);
        return Ok(created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(TKey id, TEntity entity)
    {
        var updated = await _service.UpdateAsync(entity, id!);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(TKey id)
    {
        var deleted = await _service.DeleteAsync(id!);
        return deleted ? NoContent() : NotFound();
    }
}
