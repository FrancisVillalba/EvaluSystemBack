using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VentasImpresionController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    private readonly IVentaImpresionService _ventaImpresionService;

    public VentasImpresionController(EvaluSystemDbContext context, IVentaImpresionService ventaImpresionService)
    {
        _context = context;
        _ventaImpresionService = ventaImpresionService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<VentaImpresionCabDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = Query().AsNoTracking();
        var totalItems = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResponse<VentaImpresionCabDto>(
            items.Select(x => x.ToDto()),
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VentaImpresionCabDto>> GetById(int id)
    {
        var item = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<VentaImpresionCabDto>> Create(VentaImpresionCabRequest request)
    {
        await Task.CompletedTask;
        return BadRequest(new { message = "Use POST /api/VentasImpresion/completa para crear la venta con sus detalles." });
    }

    [HttpPost("completa")]
    public async Task<ActionResult<VentaImpresionCabDto>> CreateCompleta(VentaImpresionCompletaRequest request)
    {
        var venta = await _ventaImpresionService.CrearVentaCompletaAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = venta.Id }, venta);
    }

    [HttpPut("completa/{id:int}")]
    public async Task<ActionResult<VentaImpresionCabDto>> UpdateCompleta(int id, VentaImpresionCompletaUpdateRequest request)
    {
        var venta = await _ventaImpresionService.ActualizarVentaCompletaAsync(id, request);
        return venta is null ? NotFound() : Ok(venta);
    }

    [HttpGet("{id:int}/detalles")]
    public async Task<ActionResult<IEnumerable<VentaImpresionDetDto>>> GetDetalles(int id)
    {
        if (!await _context.VentasImpresionCab.AnyAsync(x => x.Id == id))
        {
            return NotFound();
        }

        var detalles = await _context.VentasImpresionDet
            .Include(x => x.Producto)
            .Include(x => x.TipoMaquina)
            .AsNoTracking()
            .Where(x => x.CabId == id)
            .ToListAsync();

        return Ok(detalles.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}/detalles/{detalleId:int}")]
    public async Task<ActionResult<VentaImpresionDetDto>> GetDetalleById(int id, int detalleId)
    {
        var detalle = await _context.VentasImpresionDet
            .Include(x => x.Producto)
            .Include(x => x.TipoMaquina)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CabId == id && x.Id == detalleId);

        return detalle is null ? NotFound() : Ok(detalle.ToDto());
    }

    [HttpPost("{id:int}/detalles")]
    public async Task<ActionResult<VentaImpresionDetDto>> CreateDetalle(int id, VentaImpresionDetalleCreateRequest request)
    {
        var detalle = await _ventaImpresionService.CrearDetalleAsync(id, request);
        return CreatedAtAction(nameof(GetDetalleById), new { id, detalleId = detalle.Id }, detalle);
    }

    [HttpPut("{id:int}/detalles/{detalleId:int}")]
    public async Task<ActionResult<VentaImpresionDetDto>> UpdateDetalle(int id, int detalleId, VentaImpresionDetalleCreateRequest request)
    {
        var detalle = await _ventaImpresionService.ActualizarDetalleAsync(id, detalleId, request);
        return detalle is null ? NotFound() : Ok(detalle);
    }

    [HttpDelete("{id:int}/detalles/{detalleId:int}")]
    public async Task<IActionResult> DeleteDetalle(int id, int detalleId)
    {
        var eliminado = await _ventaImpresionService.EliminarDetalleAsync(id, detalleId);
        return eliminado ? NoContent() : NotFound();
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<VentaImpresionCabDto>> Update(int id, VentaImpresionCabRequest request)
    {
        var venta = await _ventaImpresionService.ActualizarCabeceraAsync(id, request);
        return venta is null ? NotFound() : Ok(venta);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var eliminado = await _ventaImpresionService.EliminarVentaAsync(id);
        return eliminado ? NoContent() : NotFound();
    }

    private IQueryable<Models.VentaImpresionCab> Query()
    {
        return _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.FormaPago)
            .Include(x => x.EstadoPago)
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }
}
