using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductoComisionesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public ProductoComisionesController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductoComisionDto>>> GetAll()
    {
        var items = await Query().OrderBy(x => x.Producto.Nombre).ThenBy(x => x.Perfil.Nombre).ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductoComisionDto>> GetById(int id)
    {
        var item = await Query().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ProductoComisionDto>> Create(ProductoComisionRequest request)
    {
        var validation = await ValidateRequestAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        var item = request.ToEntity();
        _context.ProductoComisiones.Add(item);
        await _context.SaveChangesAsync();
        await _context.Entry(item).Reference(x => x.Producto).LoadAsync();
        await _context.Entry(item).Reference(x => x.Perfil).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProductoComisionRequest request)
    {
        var item = await _context.ProductoComisiones.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var validation = await ValidateRequestAsync(request, id);
        if (validation is not null)
        {
            return validation;
        }

        request.ToEntity(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.ProductoComisiones.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.ProductoComisiones.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Models.ProductoComision> Query()
    {
        return _context.ProductoComisiones
            .Include(x => x.Producto)
            .Include(x => x.Perfil)
            .AsNoTracking();
    }

    private async Task<ActionResult?> ValidateRequestAsync(ProductoComisionRequest request, int? id = null)
    {
        if (request.FechaDesde.HasValue && request.FechaHasta.HasValue && request.FechaHasta < request.FechaDesde)
        {
            return BadRequest(new { message = "Fecha hasta debe ser mayor o igual a fecha desde." });
        }

        if (!await _context.Productos.AnyAsync(x => x.Id == request.ProductoId && x.Estado))
        {
            return BadRequest(new { message = "Producto no encontrado o inactivo." });
        }

        if (!await _context.Perfiles.AnyAsync(x => x.Id == request.PerfilId && x.Estado))
        {
            return BadRequest(new { message = "Perfil no encontrado o inactivo." });
        }

        var from = request.FechaDesde?.Date;
        var to = request.FechaHasta?.Date;
        var overlaps = await _context.ProductoComisiones
            .Where(x => !id.HasValue || x.Id != id.Value)
            .Where(x => x.Estado && request.Estado)
            .Where(x => x.ProductoId == request.ProductoId && x.PerfilId == request.PerfilId)
            .AnyAsync(x =>
                (x.FechaHasta == null || from == null || x.FechaHasta >= from) &&
                (to == null || x.FechaDesde == null || x.FechaDesde <= to));

        return overlaps
            ? BadRequest(new { message = "Ya existe una comision activa para ese producto, perfil y periodo." })
            : null;
    }
}
