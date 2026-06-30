using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CiudadesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public CiudadesController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CiudadDto>>> GetAll([FromQuery] int? departamentoId)
    {
        var query = _context.Ciudades.Include(x => x.Departamento).AsNoTracking();
        if (departamentoId.HasValue)
        {
            query = query.Where(x => x.DepartamentoId == departamentoId.Value);
        }

        var items = await query.OrderBy(x => x.DepartamentoId).ThenBy(x => x.Nombre).ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CiudadDto>> GetById(int id)
    {
        var item = await _context.Ciudades.Include(x => x.Departamento).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<CiudadDto>> Create(CiudadRequest request)
    {
        var departamento = await _context.Departamentos.FindAsync(request.DepartamentoId);
        if (departamento is null)
        {
            return BadRequest(new { message = "Debe seleccionar un departamento valido." });
        }

        var nombre = request.Nombre.Trim();
        var exists = await _context.Ciudades.AnyAsync(x => x.DepartamentoId == request.DepartamentoId && x.Nombre == nombre);
        if (exists)
        {
            return BadRequest(new { message = "Ya existe una ciudad con ese nombre en el departamento seleccionado." });
        }

        var codigoDistrito = request.CodigoDistrito.GetValueOrDefault();
        if (codigoDistrito <= 0)
        {
            codigoDistrito = (await _context.Ciudades
                .Where(x => x.DepartamentoId == request.DepartamentoId)
                .MaxAsync(x => (int?)x.CodigoDistrito) ?? 0) + 1;
        }
        else if (await _context.Ciudades.AnyAsync(x => x.DepartamentoId == request.DepartamentoId && x.CodigoDistrito == codigoDistrito))
        {
            return BadRequest(new { message = "Ya existe una ciudad con ese codigo de distrito en el departamento seleccionado." });
        }

        var item = new Ciudad
        {
            DepartamentoId = request.DepartamentoId,
            CodigoDistrito = codigoDistrito,
            Nombre = nombre,
            Estado = request.Estado
        };

        _context.Ciudades.Add(item);
        await _context.SaveChangesAsync();
        await _context.Entry(item).Reference(x => x.Departamento).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CiudadRequest request)
    {
        var item = await _context.Ciudades.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var departamento = await _context.Departamentos.FindAsync(request.DepartamentoId);
        if (departamento is null)
        {
            return BadRequest(new { message = "Debe seleccionar un departamento valido." });
        }

        var nombre = request.Nombre.Trim();
        var exists = await _context.Ciudades.AnyAsync(x =>
            x.Id != id &&
            x.DepartamentoId == request.DepartamentoId &&
            x.Nombre == nombre);
        if (exists)
        {
            return BadRequest(new { message = "Ya existe una ciudad con ese nombre en el departamento seleccionado." });
        }

        var codigoDistrito = request.CodigoDistrito.GetValueOrDefault();
        if (codigoDistrito <= 0)
        {
            codigoDistrito = item.CodigoDistrito > 0
                ? item.CodigoDistrito
                : (await _context.Ciudades
                    .Where(x => x.DepartamentoId == request.DepartamentoId)
                    .MaxAsync(x => (int?)x.CodigoDistrito) ?? 0) + 1;
        }

        var duplicatedCode = await _context.Ciudades.AnyAsync(x =>
            x.Id != id &&
            x.DepartamentoId == request.DepartamentoId &&
            x.CodigoDistrito == codigoDistrito);
        if (duplicatedCode)
        {
            return BadRequest(new { message = "Ya existe una ciudad con ese codigo de distrito en el departamento seleccionado." });
        }

        item.DepartamentoId = request.DepartamentoId;
        item.CodigoDistrito = codigoDistrito;
        item.Nombre = nombre;
        item.Estado = request.Estado;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Ciudades.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        item.Estado = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
