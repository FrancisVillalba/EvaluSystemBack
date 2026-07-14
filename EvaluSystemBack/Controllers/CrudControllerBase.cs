using EvaluSystemBack.Dtos;
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
    public async Task<IActionResult> GetAll([FromQuery] string? search = null, [FromQuery] int? page = null, [FromQuery] int pageSize = 10)
    {
        var result = await _service.GetAllAsync();
        if (!page.HasValue)
        {
            return Ok(result);
        }

        var currentPage = Math.Max(page.Value, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var filtered = result.Where(item => MatchesSearch(item, search)).ToList();
        var totalItems = filtered.Count;
        var totalPages = Math.Max((int)Math.Ceiling(totalItems / (double)pageSize), 1);
        currentPage = Math.Min(currentPage, totalPages);

        return Ok(new PagedResponse<TEntity>(
            filtered.Skip((currentPage - 1) * pageSize).Take(pageSize),
            currentPage,
            pageSize,
            totalItems,
            totalPages));
    }

    private static bool MatchesSearch(TEntity item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        return item.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string) || property.PropertyType.IsPrimitive || property.PropertyType == typeof(decimal) || property.PropertyType == typeof(DateTime))
            .Select(property => property.GetValue(item)?.ToString())
            .Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase));
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
