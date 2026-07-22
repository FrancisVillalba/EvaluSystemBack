using System.Globalization;
using System.Security.Claims;
using System.Text;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeliveryController : ControllerBase
{
    private const string MetodoEntregaDelivery = "DELIVERY";
    private const string MetodoEntregaTransportadora = "TRANSPORTADORA";
    private const string MetodoEntregaMotobolt = "MOTOBOLT";
    private const string MetodoEntregaRetiroLocal = "RETIRO_LOCAL";
    private const string EstadoRutaAbierto = "Abierto";
    private const string EstadoRutaCerrado = "Cerrado";
    private readonly EvaluSystemDbContext _context;
    private readonly IEstadoVentaFlujoService _estadoVentaFlujoService;

    public DeliveryController(EvaluSystemDbContext context, IEstadoVentaFlujoService estadoVentaFlujoService)
    {
        _context = context;
        _estadoVentaFlujoService = estadoVentaFlujoService;
    }

    [HttpGet("disponibles")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetDisponibles(CancellationToken cancellationToken)
    {
        var pedidos = await Query()
            .Where(x => x.UsuarioEntregaPedidoId == null)
            .Where(x => x.MetodoEntrega == MetodoEntregaDelivery)
            .Where(x => x.EstadoVentaId == "PE")
            .OrderBy(x => x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("transportadora")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetTransportadora(CancellationToken cancellationToken)
    {
        var pedidos = await PendingByMethodAsync(MetodoEntregaTransportadora, cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("motobolt")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetMotobolt(CancellationToken cancellationToken)
    {
        var pedidos = await PendingByMethodAsync(MetodoEntregaMotobolt, cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("retiro-local")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetRetiroLocal(CancellationToken cancellationToken)
    {
        var pedidos = await PendingByMethodAsync(MetodoEntregaRetiroLocal, cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("mis-pedidos")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetMisPedidos(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var pedidos = await Query()
            .Where(x => x.UsuarioEntregaPedidoId == userId.Value)
            .Where(x => x.MetodoEntrega == MetodoEntregaDelivery || x.MetodoEntrega == MetodoEntregaTransportadora)
            .Where(x => !_context.RutasDeliveryDetalle.Any(detalle => detalle.VentaId == x.Id))
            .OrderBy(x => x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("mis-rutas")]
    public async Task<ActionResult<IEnumerable<DeliveryRutaDto>>> GetMisRutas(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var desde = fechaDesde?.Date;
        var hastaExclusivo = fechaHasta?.Date.AddDays(1);
        var query = _context.RutasDelivery
            .AsNoTracking()
            .Include(x => x.UsuarioDelivery).ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.EstadoVenta)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.UsuarioEntregaPedido)!.ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Detalles).ThenInclude(x => x.TipoMaquina)
            .Where(x => x.UsuarioDeliveryId == userId.Value);

        if (desde.HasValue)
        {
            query = query.Where(x => x.FechaGeneracion >= desde.Value);
        }

        if (hastaExclusivo.HasValue)
        {
            query = query.Where(x => x.FechaGeneracion < hastaExclusivo.Value);
        }

        var rutas = await query
            .OrderByDescending(x => x.FechaGeneracion)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(rutas.Select(ToRutaDto));
    }

    [HttpGet("entregas")]
    public async Task<ActionResult<IEnumerable<DeliveryPedidoDto>>> GetEntregas(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        [FromQuery] int? deliveryId,
        CancellationToken cancellationToken)
    {
        var query = Query()
            .Where(x => x.UsuarioEntregaPedidoId != null);

        if (deliveryId.HasValue)
        {
            query = query.Where(x => x.UsuarioEntregaPedidoId == deliveryId.Value);
        }

        query = ApplyDeliveryDateRange(query, fechaDesde, fechaHasta);

        var pedidos = await query
            .OrderBy(x => x.UsuarioEntregaPedido!.Persona!.PrimerNombre)
            .ThenBy(x => x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }

    [HttpGet("resumen")]
    public async Task<ActionResult<IEnumerable<DeliveryResumenDto>>> GetResumen(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        CancellationToken cancellationToken)
    {
        var pedidos = await ApplyDeliveryDateRange(
                Query().Where(x => x.UsuarioEntregaPedidoId != null),
                fechaDesde,
                fechaHasta)
            .ToListAsync(cancellationToken);

        var resumen = pedidos
            .GroupBy(x => x.UsuarioEntregaPedidoId!.Value)
            .Select(group => new DeliveryResumenDto(
                group.Key,
                group.First().UsuarioEntregaPedido is null ? $"Usuario {group.Key}" : NombreUsuario(group.First().UsuarioEntregaPedido!),
                group.Count(),
                group.Sum(x => x.TotalVenta),
                string.Join(", ", group.Select(DeliveryCity).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x)),
                string.Join(", ", group.Select(x => MetodoEntregaLabel(x.MetodoEntrega)).Distinct().OrderBy(x => x))))
            .OrderBy(x => x.Delivery)
            .ToList();

        return Ok(resumen);
    }

    [HttpGet("reporte-ruta/pdf")]
    public async Task<ActionResult<ExcelFileDto>> DescargarRutaPdf([FromQuery] DateTime? fechaDesde, [FromQuery] DateTime? fechaHasta, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var desde = fechaDesde?.Date;
        var hastaExclusivo = fechaHasta?.Date.AddDays(1);

        var query = Query()
            .Where(x => x.UsuarioEntregaPedidoId == userId.Value)
            .Where(x => x.MetodoEntrega == MetodoEntregaDelivery || x.MetodoEntrega == MetodoEntregaTransportadora);

        if (desde.HasValue)
        {
            query = query.Where(x => (x.FechaEntrega ?? x.FechaCreacion) >= desde.Value);
        }

        if (hastaExclusivo.HasValue)
        {
            query = query.Where(x => (x.FechaEntrega ?? x.FechaCreacion) < hastaExclusivo.Value);
        }

        var pedidos = await query.ToListAsync(cancellationToken);

        pedidos = pedidos
            .OrderBy(DeliveryCity)
            .ThenBy(DeliveryAddress)
            .ThenBy(x => x.Id)
            .ToList();

        var usuario = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);

        var dtos = pedidos.Select(ToDto).ToList();
        var deliveryName = usuario is null ? $"Usuario {userId.Value}" : NombreUsuario(usuario);
        var bytes = DeliveryRoutePdfBuilder.Build(deliveryName, dtos, DateRangeLabel(desde, fechaHasta?.Date));

        return Ok(new ExcelFileDto(
            $"ruta-delivery-{DateTime.Now:yyyyMMddHHmm}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }

    [HttpPost("rutas/generar")]
    public async Task<ActionResult<DeliveryRutaDto>> GenerarRuta(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        [FromQuery] string? cliente,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var pedidosQuery = _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Cliente)
            .Where(x => x.UsuarioEntregaPedidoId == userId.Value)
            .Where(x => x.MetodoEntrega == MetodoEntregaDelivery || x.MetodoEntrega == MetodoEntregaTransportadora)
            .Where(x => !_context.RutasDeliveryDetalle.Any(detalle => detalle.VentaId == x.Id))
            .AsQueryable();

        var desde = fechaDesde?.Date;
        var hastaExclusivo = fechaHasta?.Date.AddDays(1);
        if (desde.HasValue)
        {
            pedidosQuery = pedidosQuery.Where(x => (x.FechaEntrega ?? x.FechaCreacion) >= desde.Value);
        }

        if (hastaExclusivo.HasValue)
        {
            pedidosQuery = pedidosQuery.Where(x => (x.FechaEntrega ?? x.FechaCreacion) < hastaExclusivo.Value);
        }

        if (!string.IsNullOrWhiteSpace(cliente))
        {
            pedidosQuery = pedidosQuery.Where(x => x.Cliente != null && x.Cliente.Nombre == cliente);
        }

        var pedidos = await pedidosQuery
            .OrderBy(x => x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (pedidos.Count == 0)
        {
            return BadRequest(new { message = "No hay pedidos tomados sin lote para generar una ruta." });
        }

        var now = DateTime.Now;
        var numeroLote = await BuildNumeroLoteAsync(now, cancellationToken);
        var ruta = new RutaDelivery
        {
            NumeroLote = numeroLote,
            UsuarioDeliveryId = userId.Value,
            FechaGeneracion = now,
            Estado = "Generado",
            Detalles = pedidos.Select(pedido => new RutaDeliveryDetalle
            {
                VentaId = pedido.Id,
                FechaAgregado = now
            }).ToList()
        };

        _context.RutasDelivery.Add(ruta);
        await _context.SaveChangesAsync(cancellationToken);

        var created = await QueryRutaById(ruta.Id)
            .FirstAsync(cancellationToken);

        return Ok(ToRutaDto(created));
    }

    [HttpGet("rutas/{id:int}/pdf")]
    public async Task<ActionResult<ExcelFileDto>> DescargarRutaLotePdf(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var ruta = await QueryRutaById(id)
            .FirstOrDefaultAsync(cancellationToken);

        if (ruta is null)
        {
            return NotFound(new { message = "No se encontro el lote de delivery." });
        }

        if (ruta.UsuarioDeliveryId != userId.Value)
        {
            return Forbid();
        }

        var pedidos = ruta.Detalles
            .Select(x => x.Venta)
            .Where(x => x is not null)
            .Select(x => ToDto(x!))
            .ToList();
        var deliveryName = ruta.UsuarioDelivery is null ? $"Usuario {ruta.UsuarioDeliveryId}" : NombreUsuario(ruta.UsuarioDelivery);
        var label = $"Lote {ruta.NumeroLote} - {ruta.FechaGeneracion:dd/MM/yyyy HH:mm}";
        var bytes = DeliveryRoutePdfBuilder.Build(deliveryName, pedidos, label);

        return Ok(new ExcelFileDto(
            $"ruta-{ruta.NumeroLote}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }

    [HttpGet("{id:int}/transportadora-etiqueta/pdf")]
    public async Task<ActionResult<ExcelFileDto>> DescargarEtiquetaTransportadora(int id, CancellationToken cancellationToken)
    {
        var pedido = await Query()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        if (!string.Equals(pedido.MetodoEntrega, MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "La etiqueta solo esta disponible para pedidos con envio por transportadora." });
        }

        var bytes = TransportadoraEtiquetaPdfBuilder.Build(pedido);
        return Ok(new ExcelFileDto(
            $"envio-transportadora-{pedido.Id}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }

    private static string DateRangeLabel(DateTime? fechaDesde, DateTime? fechaHasta)
    {
        if (fechaDesde.HasValue && fechaHasta.HasValue)
        {
            return $"{fechaDesde.Value:dd/MM/yyyy} al {fechaHasta.Value:dd/MM/yyyy}";
        }

        if (fechaDesde.HasValue)
        {
            return $"Desde {fechaDesde.Value:dd/MM/yyyy}";
        }

        if (fechaHasta.HasValue)
        {
            return $"Hasta {fechaHasta.Value:dd/MM/yyyy}";
        }

        return DateTime.Now.ToString("dd/MM/yyyy");
    }

    [HttpPost("{id:int}/tomar")]
    public async Task<ActionResult<DeliveryPedidoDto>> TomarPedido(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var pedido = await QueryForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        if (pedido.EstadoVentaId != "PE")
        {
            return BadRequest(new { message = "El pedido no esta listo para envio." });
        }

        if (!CanTakeForRoute(pedido.MetodoEntrega))
        {
            return BadRequest(new { message = "Este pedido no se puede tomar para ruta." });
        }

        if (pedido.UsuarioEntregaPedidoId.HasValue && pedido.UsuarioEntregaPedidoId.Value != userId.Value)
        {
            return BadRequest(new { message = "Este pedido ya fue tomado por otro delivery." });
        }

        pedido.UsuarioEntregaPedidoId = userId.Value;
        pedido.FechaTomaDelivery = DateTime.Now;
        await MarkAsSentAsync(pedido, userId.Value, cancellationToken);

        var updated = await Query().FirstAsync(x => x.Id == id, cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{id:int}/quitar")]
    public async Task<IActionResult> QuitarPedidoTomado(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var pedido = await QueryForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        if (pedido.UsuarioEntregaPedidoId != userId.Value)
        {
            return BadRequest(new { message = "Solo el usuario que tomo el pedido puede quitarlo." });
        }

        var tieneLote = await _context.RutasDeliveryDetalle
            .AnyAsync(x => x.VentaId == id, cancellationToken);

        if (tieneLote)
        {
            return BadRequest(new { message = "No se puede quitar un pedido que ya pertenece a un lote." });
        }

        var estadoPendienteEnvio = await _estadoVentaFlujoService.ObtenerPorIdAsync("PE", cancellationToken);

        if (estadoPendienteEnvio is null)
        {
            return BadRequest(new { message = "No existe un estado pendiente de envio configurado." });
        }

        pedido.EstadoVentaId = estadoPendienteEnvio.Id;
        pedido.UsuarioEntregaPedidoId = null;
        pedido.FechaTomaDelivery = null;
        pedido.UsuModificacion = userId.Value;
        pedido.FechaModificacion = DateTime.Now;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:int}/marcar-enviado")]
    public async Task<ActionResult<DeliveryPedidoDto>> MarcarEnviado(int id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var pedido = await QueryForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        if (!CanSendDirectly(pedido.MetodoEntrega) && pedido.UsuarioEntregaPedidoId != userId.Value)
        {
            return BadRequest(new { message = "Solo el delivery que tomo el pedido puede marcarlo como enviado." });
        }

        await MarkAsSentAsync(pedido, userId.Value, cancellationToken);

        var updated = await Query().FirstAsync(x => x.Id == id, cancellationToken);
        return Ok(ToDto(updated));
    }

    private async Task MarkAsSentAsync(VentaImpresionCab pedido, int userId, CancellationToken cancellationToken)
    {
        var estadoEnviado = await _estadoVentaFlujoService.ObtenerSiguienteAsync(pedido.EstadoVenta, cancellationToken);

        if (estadoEnviado is null)
        {
            throw new InvalidOperationException("No existe un estado de venta para enviado.");
        }

        pedido.EstadoVentaId = estadoEnviado.Id;
        pedido.UsuarioEntregaPedidoId = userId;
        pedido.UsuModificacion = userId;
        pedido.FechaModificacion = DateTime.Now;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task AddPedidoToOpenRouteAsync(int pedidoId, int userId, CancellationToken cancellationToken)
    {
        var exists = await _context.RutasDeliveryDetalle
            .AnyAsync(x => x.VentaId == pedidoId, cancellationToken);

        if (exists)
        {
            return;
        }

        var now = DateTime.Now;
        var ruta = await _context.RutasDelivery
            .Where(x => x.UsuarioDeliveryId == userId && x.Estado == EstadoRutaAbierto)
            .OrderByDescending(x => x.FechaGeneracion)
            .FirstOrDefaultAsync(cancellationToken);

        if (ruta is null)
        {
            ruta = new RutaDelivery
            {
                NumeroLote = await BuildNumeroLoteAsync(now, cancellationToken),
                UsuarioDeliveryId = userId,
                FechaGeneracion = now,
                Estado = EstadoRutaAbierto
            };

            _context.RutasDelivery.Add(ruta);
            await _context.SaveChangesAsync(cancellationToken);
        }

        _context.RutasDeliveryDetalle.Add(new RutaDeliveryDetalle
        {
            RutaDeliveryId = ruta.Id,
            VentaId = pedidoId,
            FechaAgregado = now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task CloseRouteAsync(int rutaId, int userId, CancellationToken cancellationToken)
    {
        var ruta = await _context.RutasDelivery
            .FirstOrDefaultAsync(x => x.Id == rutaId && x.UsuarioDeliveryId == userId, cancellationToken);

        if (ruta is null || ruta.Estado == EstadoRutaCerrado)
        {
            return;
        }

        ruta.Estado = EstadoRutaCerrado;
        ruta.FechaModificacion = DateTime.Now;
        ruta.UsuModificacion = userId;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<VentaImpresionCab> ApplyDeliveryDateRange(IQueryable<VentaImpresionCab> query, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var desde = fechaDesde?.Date;
        var hastaExclusivo = fechaHasta?.Date.AddDays(1);

        if (desde.HasValue)
        {
            query = query.Where(x => (x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion) >= desde.Value);
        }

        if (hastaExclusivo.HasValue)
        {
            query = query.Where(x => (x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion) < hastaExclusivo.Value);
        }

        return query;
    }

    private IQueryable<VentaImpresionCab> Query()
    {
        return _context.VentasImpresionCab
            .AsNoTracking()
            .Include(x => x.Cliente)
                .ThenInclude(x => x!.Departamento)
            .Include(x => x.Cliente)
                .ThenInclude(x => x!.Ciudad)
            .Include(x => x.Cliente)
                .ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.Cliente)
                .ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.Cliente)
                .ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Transportadora)
            .Include(x => x.EstadoVenta)
            .Include(x => x.UsuarioEntregaPedido).ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }

    private IQueryable<VentaImpresionCab> QueryForUpdate()
    {
        return _context.VentasImpresionCab
            .Include(x => x.EstadoVenta);
    }

    private IQueryable<RutaDelivery> QueryRutaById(int id)
    {
        return _context.RutasDelivery
            .AsNoTracking()
            .Include(x => x.UsuarioDelivery).ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Cliente)!.ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.EstadoVenta)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.UsuarioEntregaPedido)!.ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.Venta)!.ThenInclude(x => x!.Detalles).ThenInclude(x => x.TipoMaquina)
            .Where(x => x.Id == id);
    }

    private async Task<string> BuildNumeroLoteAsync(DateTime now, CancellationToken cancellationToken)
    {
        var prefix = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var count = await _context.RutasDelivery
            .CountAsync(x => x.NumeroLote.StartsWith(prefix), cancellationToken);

        return $"{prefix}-{count + 1:000}";
    }

    private async Task<List<VentaImpresionCab>> PendingByMethodAsync(string method, CancellationToken cancellationToken)
    {
        return await Query()
            .Where(x => x.MetodoEntrega == method)
            .Where(x => !CanTakeForRoute(method) || x.UsuarioEntregaPedidoId == null)
            .Where(x => x.EstadoVentaId == "PE")
            .OrderBy(x => x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    private static bool CanTakeForRoute(string? method)
    {
        return string.Equals(method, MetodoEntregaDelivery, StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanSendDirectly(string? method)
    {
        return string.Equals(method, MetodoEntregaMotobolt, StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, MetodoEntregaRetiroLocal, StringComparison.OrdinalIgnoreCase);
    }

    private static string MetodoEntregaLabel(string? method)
    {
        return (method ?? MetodoEntregaDelivery).ToUpperInvariant() switch
        {
            MetodoEntregaTransportadora => "Transportadora",
            MetodoEntregaMotobolt => "Motobolt",
            MetodoEntregaRetiroLocal => "Retiro del local",
            "OTRO" => "Otro",
            _ => "Delivery"
        };
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private static DeliveryPedidoDto ToDto(VentaImpresionCab pedido)
    {
        return new DeliveryPedidoDto(
            pedido.Id,
            pedido.FechaCreacion,
            pedido.FechaEntrega,
            pedido.Cliente?.Nombre ?? string.Empty,
            pedido.Cliente?.NroTelefono,
            pedido.Cliente?.DatosEnvio?.Direccion ?? pedido.Cliente?.Direccion,
            pedido.Cliente?.DatosEnvio?.Departamento?.Nombre ?? pedido.Cliente?.Departamento?.Nombre,
            pedido.Cliente?.DatosEnvio?.Ciudad?.Nombre ?? pedido.Cliente?.Ciudad?.Nombre,
            pedido.EstadoVentaId,
            pedido.EstadoVenta?.Nombre,
            $"Usuario {pedido.VendedorId}",
            pedido.TotalVenta,
            pedido.MetodoEntrega ?? MetodoEntregaDelivery,
            MetodoEntregaLabel(pedido.MetodoEntrega),
            pedido.UsuarioEntregaPedidoId,
            pedido.UsuarioEntregaPedido is null ? null : NombreUsuario(pedido.UsuarioEntregaPedido),
            pedido.FechaTomaDelivery,
            Productos(pedido));
    }

    private static DeliveryRutaDto ToRutaDto(RutaDelivery ruta)
    {
        var pedidos = ruta.Detalles
            .Select(x => x.Venta)
            .Where(x => x is not null)
            .Select(x => ToDto(x!))
            .OrderBy(x => x.Ciudad)
            .ThenBy(x => x.Id)
            .ToList();

        return new DeliveryRutaDto(
            ruta.Id,
            ruta.NumeroLote,
            ruta.UsuarioDeliveryId,
            ruta.UsuarioDelivery is null ? $"Usuario {ruta.UsuarioDeliveryId}" : NombreUsuario(ruta.UsuarioDelivery),
            ruta.FechaGeneracion,
            ruta.Estado,
            pedidos.Count,
            string.Join(", ", pedidos.Select(x => x.Ciudad).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x)),
            string.Join(", ", pedidos.Select(x => x.MetodoEntrega).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x)),
            pedidos);
    }

    private static string NombreUsuario(Usuario usuario)
    {
        if (usuario.Persona is null)
        {
            return usuario.NombreUsuario ?? $"Usuario {usuario.Id}";
        }

        var parts = new[] { usuario.Persona.PrimerNombre, usuario.Persona.SegundoNombre, usuario.Persona.PrimerApellido, usuario.Persona.SegundoApellido }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var nombre = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(nombre) ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}" : nombre;
    }

    private static string DeliveryCity(VentaImpresionCab pedido)
    {
        return pedido.Cliente?.DatosEnvio?.Ciudad?.Nombre ?? pedido.Cliente?.Ciudad?.Nombre ?? string.Empty;
    }

    private static string DeliveryAddress(VentaImpresionCab pedido)
    {
        return pedido.Cliente?.DatosEnvio?.Direccion ?? pedido.Cliente?.Direccion ?? string.Empty;
    }

    private static string Productos(VentaImpresionCab pedido)
    {
        return string.Join(", ", pedido.Detalles
            .GroupBy(x => x.Producto?.Nombre ?? x.TipoMaquina?.Nombre ?? $"Detalle {x.Id}")
            .Select(x => $"{x.Key} {Cantidad(x.Sum(d => d.Cantidad))}"));
    }

    private static string Cantidad(decimal value)
    {
        return value.ToString("N2", CultureInfo.CurrentCulture).TrimEnd('0').TrimEnd(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]);
    }

    private static class TransportadoraEtiquetaPdfBuilder
    {
        private const double PageWidth = 226.77;
        private const double PageHeight = 141.73;

        public static byte[] Build(VentaImpresionCab pedido)
        {
            var envio = pedido.Cliente?.DatosEnvio;
            var builder = new StringBuilder();
            Text(builder, 10, 119, $"PEDIDO #{pedido.Id}", 10);
            LabelValue(builder, 10, 96, "RECIBE:", Trim(envio?.NombreReceptor ?? pedido.Cliente?.Nombre ?? "S/D", 22), 47);
            LabelValue(builder, 10, 73, "DOC:", Trim(envio?.DocumentoReceptor ?? "S/D", 16), 31);
            LabelValue(builder, 122, 73, "TEL:", Trim(envio?.TelefonoReceptor ?? pedido.Cliente?.NroTelefono ?? "S/D", 14), 27);
            LabelValue(builder, 10, 51, "DEPARTAMENTO:", Trim(envio?.Departamento?.Nombre ?? pedido.Cliente?.Departamento?.Nombre ?? "S/D", 20), 94);
            LabelValue(builder, 10, 29, "CIUDAD:", Trim(envio?.Ciudad?.Nombre ?? pedido.Cliente?.Ciudad?.Nombre ?? "S/D", 24), 54);
            LabelValue(builder, 10, 8, "DIR:", Trim(envio?.Direccion ?? pedido.Cliente?.Direccion ?? string.Empty, 32), 31);

            return WritePdf(builder.ToString());
        }

        private static void LabelValue(StringBuilder builder, double x, double y, string label, string value, double labelWidth)
        {
            Text(builder, x, y, label, 9, bold: true);
            Text(builder, x + labelWidth, y, value, 9);
        }

        private static void Text(StringBuilder builder, double x, double y, string text, int size, bool bold = false)
        {
            builder
                .AppendLine("0 0 0 rg")
                .Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(size).Append(" Tf ")
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(" Td (")
                .Append(Escape(text))
                .AppendLine(") Tj ET");
        }

        private static byte[] WritePdf(string content)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [6 0 R] /Count 1 >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents 5 0 R >>"
            };

            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true);
            var offsets = new List<long> { 0 };
            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(stream.Position);
                writer.Write(i + 1);
                writer.WriteLine(" 0 obj");
                writer.WriteLine(objects[i]);
                writer.WriteLine("endobj");
                writer.Flush();
            }

            var xref = stream.Position;
            writer.WriteLine("xref");
            writer.WriteLine($"0 {objects.Count + 1}");
            writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }

            writer.WriteLine("trailer");
            writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
            writer.WriteLine("startxref");
            writer.WriteLine(xref);
            writer.WriteLine("%%EOF");
            writer.Flush();
            return stream.ToArray();
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty)
                .Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .Select(character => character <= 127 ? character : '?')
                .Aggregate(new StringBuilder(), (builder, character) => builder.Append(character))
                .ToString()
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        private static string Trim(string value, int length)
        {
            return value.Length <= length ? value : $"{value[..Math.Max(0, length - 3)]}...";
        }
    }

    private static class DeliveryRoutePdfBuilder
    {
        private const double PageWidth = 595;
        private const double PageHeight = 842;
        private const double MarginX = 46;
        private const double TealR = 0.18;
        private const double TealG = 0.54;
        private const double TealB = 0.62;

        public static byte[] Build(string deliveryName, IReadOnlyList<DeliveryPedidoDto> pedidos, string dateRange)
        {
            return WritePdf(BuildPages(deliveryName, pedidos, dateRange));
        }

        private static List<string> BuildPages(string deliveryName, IReadOnlyList<DeliveryPedidoDto> pedidos, string dateRange)
        {
            var pages = new List<string>();
            var builder = NewPage(deliveryName, dateRange);
            var y = 710d;

            if (pedidos.Count == 0)
            {
                Text(builder, MarginX, y, "No hay pedidos tomados para armar la ruta.", 12, bold: true);
                pages.Add(builder.ToString());
                return pages;
            }

            var transportadoraPedidos = pedidos
                .Where(IsTransportadora)
                .OrderBy(x => x.Ciudad)
                .ThenBy(x => x.Id)
                .ToList();

            var deliveryPedidos = pedidos
                .Where(x => !IsTransportadora(x))
                .OrderBy(x => x.Ciudad)
                .ThenBy(x => x.Id)
                .ToList();

            if (transportadoraPedidos.Count > 0)
            {
                if (y < 155)
                {
                    pages.Add(builder.ToString());
                    builder = NewPage(deliveryName, dateRange);
                    y = 710;
                }

                Text(builder, MarginX, y, "TRANSPORTADORA", 11, bold: true, color: (TealR, TealG, TealB));
                Text(builder, MarginX + 145, y, $"{transportadoraPedidos.Count} envios", 9);
                y -= 14;
                DrawTableHeader(builder, y);
                y -= 15;

                foreach (var pedido in transportadoraPedidos)
                {
                    if (y < 55)
                    {
                        pages.Add(builder.ToString());
                        builder = NewPage(deliveryName, dateRange);
                        y = 710;
                        Text(builder, MarginX, y, "TRANSPORTADORA (CONT.)", 11, bold: true, color: (TealR, TealG, TealB));
                        y -= 14;
                        DrawTableHeader(builder, y);
                        y -= 15;
                    }

                    DrawTableRow(builder, y, pedido);
                    y -= 15;
                }

                y -= 10;
            }

            foreach (var cityGroup in deliveryPedidos
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Ciudad) ? "Sin ciudad" : x.Ciudad)
                .OrderBy(x => x.Key))
            {
                if (y < 130)
                {
                    pages.Add(builder.ToString());
                    builder = NewPage(deliveryName, dateRange);
                    y = 710;
                }

                Text(builder, MarginX, y, $"CIUDAD: {cityGroup.Key.ToUpperInvariant()}", 11, bold: true, color: (0.05, 0.23, 0.39));
                Text(builder, MarginX + 190, y, $"{cityGroup.Count()} envios", 9);
                y -= 14;
                DrawTableHeader(builder, y);
                y -= 15;

                foreach (var pedido in cityGroup)
                {
                    if (y < 55)
                    {
                        pages.Add(builder.ToString());
                        builder = NewPage(deliveryName, dateRange);
                        y = 710;
                        Text(builder, MarginX, y, $"CIUDAD: {cityGroup.Key.ToUpperInvariant()} (CONT.)", 11, bold: true, color: (0.05, 0.23, 0.39));
                        y -= 14;
                        DrawTableHeader(builder, y);
                        y -= 15;
                    }

                    DrawTableRow(builder, y, pedido);
                    y -= 15;
                }

                y -= 10;
            }

            pages.Add(builder.ToString());
            return pages;
        }

        private static bool IsTransportadora(DeliveryPedidoDto pedido)
        {
            return string.Equals(pedido.MetodoEntregaId, MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pedido.MetodoEntrega, "Transportadora", StringComparison.OrdinalIgnoreCase);
        }

        private static StringBuilder NewPage(string deliveryName, string dateRange)
        {
            var builder = new StringBuilder();
            Text(builder, 0, 790, "RUTA DE DELIVERY", 18, bold: true, centered: true, color: (TealR, TealG, TealB));
            Text(builder, MarginX, 745, deliveryName.ToUpperInvariant(), 13, bold: true);
            Text(builder, PageWidth - MarginX - 150, 745, $"Rango: {dateRange}", 11, bold: true);
            return builder;
        }

        private static void DrawTableHeader(StringBuilder builder, double y)
        {
            FillRect(builder, MarginX, y, 505, 14, TealR, TealG, TealB);
            Text(builder, MarginX + 8, y + 4, "Nro. Pedido", 7, bold: true, color: (1, 1, 1));
            Text(builder, MarginX + 93, y + 4, "Nombre", 7, bold: true, color: (1, 1, 1));
            Text(builder, MarginX + 255, y + 4, "Telefono", 7, bold: true, color: (1, 1, 1));
            Text(builder, MarginX + 365, y + 4, "Producto", 7, bold: true, color: (1, 1, 1));
        }

        private static void DrawTableRow(StringBuilder builder, double y, DeliveryPedidoDto pedido)
        {
            StrokeRect(builder, MarginX, y, 75, 15, 0.78, 0.86, 0.92);
            StrokeRect(builder, MarginX + 75, y, 160, 15, 0.78, 0.86, 0.92);
            StrokeRect(builder, MarginX + 235, y, 105, 15, 0.78, 0.86, 0.92);
            StrokeRect(builder, MarginX + 340, y, 165, 15, 0.78, 0.86, 0.92);
            Text(builder, MarginX + 8, y + 4, pedido.Id.ToString(CultureInfo.InvariantCulture), 7);
            Text(builder, MarginX + 83, y + 4, Truncate(pedido.Cliente, 24), 7);
            Text(builder, MarginX + 243, y + 4, Truncate(pedido.Telefono ?? "S/D", 15), 7);
            Text(builder, MarginX + 348, y + 4, Truncate(pedido.Productos, 28), 7);
        }

        private static byte[] WritePdf(IReadOnlyList<string> pageContents)
        {
            var objects = new List<string>();
            var contentObjectIds = new List<int>();

            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add(string.Empty);
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

            foreach (var content in pageContents)
            {
                contentObjectIds.Add(objects.Count + 1);
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
            }

            var pageObjectIds = new List<int>();
            foreach (var contentId in contentObjectIds)
            {
                pageObjectIds.Add(objects.Count + 1);
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            }

            objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pageObjectIds.Count} >>";
            return WriteObjects(objects);
        }

        private static void Text(StringBuilder builder, double x, double y, string text, int size, bool bold = false, bool centered = false, (double R, double G, double B)? color = null)
        {
            var colorValue = color ?? (0d, 0d, 0d);
            var textValue = Escape(text);
            if (centered)
            {
                var approxWidth = textValue.Length * size * 0.52;
                x = (PageWidth - approxWidth) / 2;
            }

            builder
                .Append(colorValue.R.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(colorValue.G.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(colorValue.B.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(" rg")
                .Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(size).Append(" Tf ")
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(" Td (")
                .Append(textValue)
                .AppendLine(") Tj ET");
        }

        private static void FillRect(StringBuilder builder, double x, double y, double width, double height, double r, double g, double b)
        {
            builder
                .Append(r.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(g.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(b.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(" rg")
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(height.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine(" re f");
        }

        private static void StrokeRect(StringBuilder builder, double x, double y, double width, double height, double r, double g, double b)
        {
            builder
                .Append(r.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(g.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(b.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(" RG")
                .Append("0.6 w ")
                .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append(height.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine(" re S");
        }

        private static byte[] WriteObjects(IReadOnlyList<string> objects)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true);
            var offsets = new List<long> { 0 };

            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(stream.Position);
                writer.Write(i + 1);
                writer.WriteLine(" 0 obj");
                writer.WriteLine(objects[i]);
                writer.WriteLine("endobj");
                writer.Flush();
            }

            var xref = stream.Position;
            writer.WriteLine("xref");
            writer.WriteLine($"0 {objects.Count + 1}");
            writer.WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }

            writer.WriteLine("trailer");
            writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
            writer.WriteLine("startxref");
            writer.WriteLine(xref);
            writer.WriteLine("%%EOF");
            writer.Flush();
            return stream.ToArray();
        }

        private static string Escape(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var ascii = new string(normalized
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .Select(character => character <= 127 ? character : '?')
                .ToArray());
            return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        private static string Truncate(string text, int length)
        {
            return text.Length <= length ? text : $"{text[..Math.Max(0, length - 3)]}...";
        }
    }
}
