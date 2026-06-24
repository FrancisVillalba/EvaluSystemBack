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
    public async Task<ActionResult<PagedResponse<VentaImpresionCabDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? clienteId = null,
        [FromQuery] string? estadoVentaId = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = Query().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Id.ToString().Contains(term) ||
                (x.Cliente != null && x.Cliente.Nombre != null && x.Cliente.Nombre.Contains(term)) ||
                (x.EstadoVenta != null && x.EstadoVenta.Nombre != null && x.EstadoVenta.Nombre.Contains(term)) ||
                (x.FormaPago != null && x.FormaPago.Nombre != null && x.FormaPago.Nombre.Contains(term)) ||
                x.Detalles.Any(d =>
                    (d.Producto != null && d.Producto.Nombre.Contains(term)) ||
                    (d.TipoMaquina != null && d.TipoMaquina.Nombre.Contains(term))));
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(x => x.FechaCreacion.Date >= dateFrom.Value.Date);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(x => x.FechaCreacion.Date <= dateTo.Value.Date);
        }

        if (clienteId.HasValue)
        {
            query = query.Where(x => x.ClienteId == clienteId.Value);
        }

        if (!string.IsNullOrWhiteSpace(estadoVentaId))
        {
            query = query.Where(x => x.EstadoVentaId == estadoVentaId);
        }

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

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboard()
    {
        var ventas = await Query().AsNoTracking().ToListAsync();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var ventasDelDia = ventas.Where(x => x.FechaCreacion.Date == today).ToList();
        var ventasDelMes = ventas.Where(x => x.FechaCreacion >= monthStart && x.FechaCreacion < nextMonthStart).ToList();
        var vendedores = await _context.Personas
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => NombrePersona(x));

        var pedidosImpresos = ventasDelDia.Count(x => IsPrinted(x.EstadoVenta?.Nombre) || x.Detalles.Any(d => d.CheckImpresion == true));
        var pedidosEntregados = ventasDelDia.Count(x => IsDelivered(x.EstadoVenta?.Nombre));
        var pedidosPorMaquina = ventasDelDia
            .SelectMany(x => x.Detalles.Select(d => new
            {
                d.CabId,
                Maquina = d.TipoMaquina?.Nombre ?? "Sin maquina"
            }))
            .GroupBy(x => x.Maquina)
            .Select(x => new DashboardMachineDto(x.Key, x.Select(item => item.CabId).Distinct().Count()))
            .OrderByDescending(x => x.Cantidad)
            .Take(6)
            .ToList();

        var pendientesPago = ventas
            .Select(x => new
            {
                Cliente = x.Cliente?.Nombre ?? "Sin cliente",
                Pendiente = Math.Max(x.TotalVenta - (x.MontoPagado ?? 0), 0)
            })
            .Where(x => x.Pendiente > 0)
            .GroupBy(x => x.Cliente)
            .Select(x => new DashboardMoneyDto(x.Key, x.Sum(item => item.Pendiente)))
            .OrderByDescending(x => x.Monto)
            .Take(7)
            .ToList();

        var mejoresVendedores = ventasDelMes
            .GroupBy(x => x.VendedorId)
            .Select(x => new DashboardSellerDto(
                vendedores.TryGetValue(x.Key, out var nombre) ? nombre : $"Vendedor {x.Key}",
                x.Count()))
            .OrderByDescending(x => x.Cantidad)
            .Take(7)
            .ToList();

        return Ok(new DashboardSummaryDto(
            ventasDelDia.Count,
            ventasDelDia.Count,
            pedidosImpresos,
            Math.Max(ventasDelDia.Count - pedidosImpresos, 0),
            pedidosEntregados,
            pedidosPorMaquina,
            pendientesPago,
            mejoresVendedores));
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

    private static string NombrePersona(Models.Persona persona)
    {
        var nombre = string.Join(" ", new[]
        {
            persona.PrimerNombre,
            persona.SegundoNombre,
            persona.PrimerApellido,
            persona.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(nombre) ? $"Vendedor {persona.Id}" : nombre;
    }

    private static bool IsPrinted(string? estado)
    {
        return estado?.Contains("impres", StringComparison.OrdinalIgnoreCase) == true
            || estado?.Contains("entreg", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsDelivered(string? estado)
    {
        return estado?.Contains("entreg", StringComparison.OrdinalIgnoreCase) == true;
    }
}
