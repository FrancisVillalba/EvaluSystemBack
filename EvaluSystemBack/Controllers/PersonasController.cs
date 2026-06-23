using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PersonasController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public PersonasController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PersonaDto>>> GetAll()
    {
        var items = await _context.Personas.Include(x => x.Perfil).AsNoTracking().ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PersonaDto>> GetById(int id)
    {
        var item = await _context.Personas.Include(x => x.Perfil).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<PersonaDto>> Create(PersonaRequest request)
    {
        var item = request.ToEntity();
        _context.Personas.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PersonaRequest request)
    {
        var item = await _context.Personas.FindAsync(id);
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
        var item = await _context.Personas.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.Personas.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
