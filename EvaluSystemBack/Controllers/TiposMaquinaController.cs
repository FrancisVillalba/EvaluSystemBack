using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TiposMaquinaController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public TiposMaquinaController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TipoMaquinaDto>>> GetAll()
    {
        var items = await _context.TiposMaquina.AsNoTracking().ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TipoMaquinaDto>> GetById(int id)
    {
        var item = await _context.TiposMaquina.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<TipoMaquinaDto>> Create(TipoMaquinaRequest request)
    {
        var item = request.ToEntity();
        _context.TiposMaquina.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TipoMaquinaRequest request)
    {
        var item = await _context.TiposMaquina.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        request.ToEntity(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.TiposMaquina.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.TiposMaquina.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
