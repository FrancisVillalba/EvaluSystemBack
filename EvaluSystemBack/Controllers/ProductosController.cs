using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductosController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public ProductosController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductoDto>>> GetAll()
    {
        var items = await _context.Productos.Include(x => x.TipoMaquina).AsNoTracking().ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductoDto>> GetById(int id)
    {
        var item = await _context.Productos.Include(x => x.TipoMaquina).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ProductoDto>> Create(ProductoRequest request)
    {
        var item = request.ToEntity();
        _context.Productos.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProductoRequest request)
    {
        var item = await _context.Productos.FindAsync(id);
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
        var item = await _context.Productos.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.Productos.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
