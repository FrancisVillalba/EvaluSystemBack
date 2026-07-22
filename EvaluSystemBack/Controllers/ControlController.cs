using System.Security.Claims;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ControlController : ControllerBase
{

    private readonly EvaluSystemDbContext _context;
    private readonly IPermisoService _permisoService;
    private readonly IEstadoVentaFlujoService _estadoVentaFlujoService;

    public ControlController(EvaluSystemDbContext context, IPermisoService permisoService, IEstadoVentaFlujoService estadoVentaFlujoService)
    {
        _context = context;
        _permisoService = permisoService;
        _estadoVentaFlujoService = estadoVentaFlujoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ControlPedidoDto>>> GetAll(CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("ver"))
        {
            return Forbid();
        }

        var estadoControl = await _estadoVentaFlujoService.ObtenerPorIdAsync("CO", cancellationToken);
        if (estadoControl is null)
        {
            return Ok(Array.Empty<ControlPedidoDto>());
        }

        var pedidos = await Query()
            .Where(x => x.EstadoVentaId == estadoControl.Id)
            .OrderBy(x => x.Detalles.Min(d => d.TipoMaquina!.Nombre))
            .ThenBy(x => x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(300)
            .ToListAsync(cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpPost("{id:int}/aprobar")]
    public async Task<ActionResult<ControlPedidoDto>> Aprobar(int id, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("editar"))
        {
            return Forbid();
        }

        var pedido = await QueryForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        var estadoControl = await _estadoVentaFlujoService.ObtenerPorIdAsync("CO", cancellationToken);
        if (estadoControl is not null && pedido.EstadoVentaId != estadoControl.Id)
        {
            return BadRequest(new { message = "El pedido no esta en control." });
        }

        var siguienteEstado = await _estadoVentaFlujoService.ObtenerSiguienteAsync(pedido.EstadoVenta, cancellationToken);
        if (siguienteEstado is null)
        {
            return BadRequest(new { message = "No existe un siguiente estado de venta configurado." });
        }

        pedido.EstadoVentaId = siguienteEstado.Id;
        pedido.FechaModificacion = DateTime.Now;
        pedido.UsuModificacion = CurrentUserId() ?? pedido.UsuModificacion;

        await _context.SaveChangesAsync(cancellationToken);

        var updated = await Query().FirstAsync(x => x.Id == id, cancellationToken);
        return Ok(ToDto(updated));
    }

    private IQueryable<VentaImpresionCab> Query()
    {
        return _context.VentasImpresionCab
            .AsNoTracking()
            .Include(x => x.Cliente)
            .Include(x => x.EstadoVenta)
            .Include(x => x.MetodoEnvio)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }

    private IQueryable<VentaImpresionCab> QueryForUpdate()
    {
        return _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }

    private async Task<bool> TienePermisoAsync(string accion)
    {
        var userId = CurrentUserId();
        return userId.HasValue && await _permisoService.UsuarioTienePermisoAsync(userId.Value, "Control", accion);
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private static ControlPedidoDto ToDto(VentaImpresionCab pedido)
    {
        return new ControlPedidoDto(
            pedido.Id,
            pedido.FechaCreacion,
            pedido.FechaEntrega,
            pedido.Cliente?.Nombre ?? string.Empty,
            pedido.EstadoVentaId,
            pedido.EstadoVenta?.Nombre,
            pedido.MetodoEntrega,
            pedido.MetodoEnvio?.Nombre ?? pedido.MetodoEntrega,
            pedido.TotalVenta,
            pedido.Detalles
                .OrderBy(x => x.TipoMaquina?.Nombre)
                .ThenBy(x => x.Producto?.Nombre)
                .Select(ToDetalleDto));
    }

    private static ControlPedidoDetalleDto ToDetalleDto(VentaImpresionDet detalle)
    {
        return new ControlPedidoDetalleDto(
            detalle.Id,
            detalle.CabId,
            detalle.TipoMaquinaId,
            detalle.TipoMaquina?.Nombre ?? $"Maquina {detalle.TipoMaquinaId}",
            detalle.Producto?.Nombre ?? $"Producto {detalle.ProductoId}",
            detalle.Cantidad,
            detalle.Observacion,
            detalle.CheckImpresion == true);
    }
}