using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransportadorasController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public TransportadorasController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransportadoraDto>>> GetAll()
    {
        var items = await _context.Transportadoras.AsNoTracking().ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TransportadoraDto>> GetById(int id)
    {
        var item = await _context.Transportadoras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<TransportadoraDto>> Create(TransportadoraRequest request)
    {
        var item = request.ToEntity();
        _context.Transportadoras.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TransportadoraRequest request)
    {
        var item = await _context.Transportadoras.FindAsync(id);
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
        var item = await _context.Transportadoras.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.Transportadoras.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
