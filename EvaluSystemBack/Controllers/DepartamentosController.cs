using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartamentosController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public DepartamentosController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartamentoDto>>> GetAll()
    {
        var items = await _context.Departamentos.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartamentoDto>> GetById(int id)
    {
        var item = await _context.Departamentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<DepartamentoDto>> Create(DepartamentoRequest request)
    {
        var nombre = request.Nombre.Trim();
        var exists = await _context.Departamentos.AnyAsync(x => x.Nombre == nombre);
        if (exists)
        {
            return BadRequest(new { message = "Ya existe un departamento con ese nombre." });
        }

        var id = request.Id.GetValueOrDefault();
        if (id <= 0)
        {
            id = (await _context.Departamentos.MaxAsync(x => (int?)x.Id) ?? 0) + 1;
        }
        else if (await _context.Departamentos.AnyAsync(x => x.Id == id))
        {
            return BadRequest(new { message = "Ya existe un departamento con ese codigo." });
        }

        var item = new Departamento
        {
            Id = id,
            Nombre = nombre,
            Estado = request.Estado
        };

        _context.Departamentos.Add(item);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, DepartamentoRequest request)
    {
        var item = await _context.Departamentos.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var nombre = request.Nombre.Trim();
        var exists = await _context.Departamentos.AnyAsync(x => x.Id != id && x.Nombre == nombre);
        if (exists)
        {
            return BadRequest(new { message = "Ya existe un departamento con ese nombre." });
        }

        item.Nombre = nombre;
        item.Estado = request.Estado;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Departamentos.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        item.Estado = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
