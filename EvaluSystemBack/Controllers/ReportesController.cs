using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportesController : ControllerBase
{
    private const string TipoPagoComisiones = "COMISIONES";
    private const string EstadoLoteGenerado = "Generado";
    private const string EstadoLotePagado = "Pagado";
    private const string EstadoLoteAnulado = "Anulado";
    private static readonly string[] EstadosVentaComisionables = ["CO", "EE", "ET", "PE", "PI"];
    private readonly EvaluSystemDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public ReportesController(EvaluSystemDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [HttpGet("comisiones-vendedores")]
    public async Task<ActionResult<ReporteComisionesDto>> GetComisionesVendedores(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null,
        [FromQuery] string? scope = null,
        [FromQuery] int? perfilId = null)
    {
        return Ok(await BuildComisionesAsync(dateFrom, dateTo, vendedorId, scope, perfilId));
    }

    [HttpGet("comisiones-vendedores/excel")]
    public async Task<ActionResult<ExcelFileDto>> ExportComisionesExcel(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null,
        [FromQuery] string? scope = null,
        [FromQuery] int? perfilId = null)
    {
        var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId, scope, perfilId);
        var bytes = BuildComisionesXlsx(report);
        var sellerFilePart = await ReportFileSellerNameAsync(vendedorId, report);

        return Ok(new ExcelFileDto(
            $"{sellerFilePart}-{DateTime.Now:yyyyMMddHHmm}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Convert.ToBase64String(bytes)));
    }

    [HttpGet("comisiones-vendedores/pdf")]
    public async Task<ActionResult<ExcelFileDto>> ExportComisionesPdf(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null,
        [FromQuery] string? scope = null,
        [FromQuery] int? perfilId = null,
        [FromQuery] int? vendedorExternoId = null)
    {
        var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId, scope, perfilId, vendedorExternoId);
        var isExternalSellerDetail = IsExternalCommissionsScope(scope) && vendedorExternoId.HasValue;
        var bytes = CommissionPdfBuilder.Build(
            report,
            _environment.WebRootPath,
            IsTeamLeaderCommissionsScope(scope) || IsExternalCommissionsScope(scope),
            isExternalSellerDetail);
        var sellerFilePart = await ReportFileSellerNameAsync(vendedorId, report);

        return Ok(new ExcelFileDto(
            $"{sellerFilePart}-{DateTime.Now:yyyyMMddHHmm}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }

    [HttpGet("presupuesto-pedido/{id:int}")]
    [SkipPermission]
    public async Task<ActionResult<ExcelFileDto>> ExportPresupuestoPedidoPdf(int id, CancellationToken cancellationToken)
    {
        var pedido = await _context.VentasImpresionCab
            .AsNoTracking()
            .Include(x => x.Cliente)
            .Include(x => x.FormaPago)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (pedido is null)
        {
            return NotFound(new { message = "No se encontro el pedido." });
        }

        var vendedor = await _context.Usuarios
            .AsNoTracking()
            .Where(x => x.Id == pedido.VendedorId)
            .Select(x => x.Persona == null
                ? x.NombreUsuario ?? $"Usuario {x.Id}"
                : ((x.Persona.PrimerNombre ?? "") + " " + (x.Persona.PrimerApellido ?? "")).Trim())
            .FirstOrDefaultAsync(cancellationToken) ?? $"Usuario {pedido.VendedorId}";

        var bytes = CommissionPdfBuilder.BuildBudget(pedido, vendedor, _environment.WebRootPath);
        return Ok(new ExcelFileDto(
            $"presupuesto-pedido-{pedido.Id}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }
    [HttpGet("comisiones-vendedores/txt")]
    public async Task<ActionResult<ExcelFileDto>> ExportComisionesBancoTxt(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null,
        [FromQuery] string? scope = null,
        [FromQuery] int? perfilId = null)
    {
        try
        {
            var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId, scope, perfilId);
            var lote = await GetOrCreateComisionesLoteAsync(report, vendedorId, scope, perfilId);

            return Ok(new ExcelFileDto(
                lote.NombreArchivo,
                "text/plain",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(lote.ContenidoTxt))));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("lotes-pago")]
    public async Task<ActionResult<PagedResponse<LotePagoDto>>> GetLotesPago(
        [FromQuery] string? tipoPago = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? estado = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var effectiveFrom = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var effectiveToExclusive = (dateTo ?? DateTime.Today).Date.AddDays(1);
        var normalizedEstado = string.IsNullOrWhiteSpace(estado) ? null : NormalizeLoteEstado(estado);

        var query = _context.LotesPago
            .Include(x => x.UsuarioGenero).ThenInclude(x => x!.Persona)
            .Include(x => x.Vendedor).ThenInclude(x => x!.Persona)
            .Include(x => x.Perfil)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tipoPago))
        {
            query = query.Where(x => x.TipoPago == tipoPago);
        }

        query = query.Where(x => x.FechaGeneracion >= effectiveFrom && x.FechaGeneracion < effectiveToExclusive);

        if (!string.IsNullOrWhiteSpace(normalizedEstado))
        {
            query = query.Where(x => x.Estado == normalizedEstado);
        }

        var totalItems = await query.CountAsync();
        var totalPages = Math.Max((int)Math.Ceiling(totalItems / (double)pageSize), 1);
        page = Math.Min(page, totalPages);
        var lotes = await query
            .OrderByDescending(x => x.FechaGeneracion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResponse<LotePagoDto>(
            lotes.Select(ToLotePagoDto),
            page,
            pageSize,
            totalItems,
            totalPages));
    }

    [HttpGet("lotes-pago/{id:int}/txt")]
    public async Task<ActionResult<ExcelFileDto>> DownloadLotePagoTxt(int id)
    {
        var lote = await _context.LotesPago.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (lote is null)
        {
            return NotFound(new { message = "No se encontro el lote de pago." });
        }

        return Ok(new ExcelFileDto(
            lote.NombreArchivo,
            "text/plain",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(lote.ContenidoTxt))));
    }

    [HttpPut("lotes-pago/{id:int}/estado")]
    public async Task<IActionResult> UpdateLotePagoEstado(int id, LotePagoEstadoRequest request)
    {
        var lote = await _context.LotesPago.FirstOrDefaultAsync(x => x.Id == id);
        if (lote is null)
        {
            return NotFound(new { message = "No se encontro el lote de pago." });
        }

        var estado = NormalizeLoteEstado(request.Estado);
        lote.Estado = estado;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("clientes-deuda")]
    public async Task<ActionResult<ReporteClientesDeudaDto>> GetClientesDeuda(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? cliente = null,
        [FromQuery] string? estadoPago = null,
        [FromQuery] int? vendedorId = null)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);
        var clientSearch = (cliente ?? string.Empty).Trim();
        var paymentStatus = (estadoPago ?? string.Empty).Trim().ToUpperInvariant();

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.EstadoPago)
            .Include(x => x.EstadoVenta)
            .AsNoTracking()
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .Where(x => !x.Reposicion)
            .Where(x => x.EstadoPagadoId == "P1" || x.EstadoPagadoId == "P2" || x.EstadoPagadoId == "P3")
            .Where(x => !vendedorId.HasValue || x.VendedorId == vendedorId.Value)
            .Where(x => string.IsNullOrWhiteSpace(paymentStatus)
                || (paymentStatus == "PENDIENTE_PARCIAL" && (x.EstadoPagadoId == "P1" || x.EstadoPagadoId == "P2"))
                || x.EstadoPagadoId == paymentStatus)
            .OrderBy(x => x.ClienteId)
            .ThenByDescending(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(x => x.EstadoVenta?.Nombre?.Contains("elimin", StringComparison.OrdinalIgnoreCase) != true)
            .Where(x => string.IsNullOrWhiteSpace(clientSearch) ||
                (x.Cliente?.Nombre ?? string.Empty).Contains(clientSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var vendedorIds = ventas.Select(x => x.VendedorId).Distinct().ToArray();
        var vendedores = await _context.Usuarios
            .AsNoTracking()
            .Include(x => x.Persona)
            .Where(x => vendedorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, NombreUsuario);

        var clientes = ventas
            .GroupBy(x => new
            {
                x.ClienteId,
                Cliente = x.Cliente?.Nombre ?? $"Cliente {x.ClienteId}",
                Telefono = x.Cliente?.NroTelefono
            })
            .Select(group =>
            {
                var pedidos = group.Select(x => new ReporteClienteDeudaPedidoDto(
                    x.Id,
                    x.FechaCreacion,
                    vendedores.GetValueOrDefault(x.VendedorId, $"Usuario {x.VendedorId}"),
                    x.TotalVenta,
                    x.MontoPagado ?? 0,
                    Math.Max(x.TotalVenta - (x.MontoPagado ?? 0), 0),
                    x.EstadoPago?.Nombre ?? (x.EstadoPagadoId == "P3" ? "Pagado" : x.EstadoPagadoId == "P2" ? "Pago parcial" : "Pendiente de pago")))
                    .OrderByDescending(x => x.Fecha)
                    .ToList();
                var estados = pedidos.Select(x => x.EstadoPago).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var estadoIds = group.Select(x => x.EstadoPagadoId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var estadoResumen = estados.Count == 1
                    ? estados[0]
                    : estadoIds.Count == 2 && estadoIds.Contains("P1") && estadoIds.Contains("P2")
                        ? "Pendiente / Parcial"
                        : string.Join(" / ", estados);
                return new ReporteClienteDeudaDto(
                    group.Key.ClienteId, group.Key.Cliente, group.Key.Telefono, pedidos.Count,
                    pedidos.Sum(x => x.TotalVenta), pedidos.Sum(x => x.MontoPagado),
                    pedidos.Sum(x => x.SaldoPendiente), pedidos.Max(x => x.Fecha),
                    estadoResumen, pedidos);
            })
            .OrderByDescending(x => x.SaldoPendiente)
            .ToList();

        return Ok(new ReporteClientesDeudaDto(
            from, to, clientes.Sum(x => x.SaldoPendiente), clientes.Count,
            clientes.Sum(x => x.CantidadPedidos), clientes.Sum(x => x.TotalVendido),
            clientes.Sum(x => x.TotalPagado), clientes));
    }
    [HttpGet("envios")]
    public async Task<ActionResult<ReporteEnviosDto>> GetReporteEnvios(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? cliente = null,
        [FromQuery] string? metodoEntrega = null)
    {
        var from = (dateFrom ?? DateTime.Today).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);
        var clientSearch = (cliente ?? string.Empty).Trim();
        var method = (metodoEntrega ?? string.Empty).Trim().ToUpperInvariant();

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente).ThenInclude(x => x!.Ciudad)
            .Include(x => x.Cliente).ThenInclude(x => x!.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .Include(x => x.EstadoVenta)
            .Include(x => x.UsuarioEntregaPedido).ThenInclude(x => x!.Persona)
            .AsNoTracking()
            .Where(x => x.FechaModificacion >= from)
            .Where(x => x.FechaModificacion < toExclusive)
            .Where(x => x.EstadoVentaId == "ET" || x.Detalles.Any(d => d.EstadoItem == "EE" || d.EstadoItem == "ET"))
            .Where(x => string.IsNullOrWhiteSpace(method) || x.MetodoEntrega == method)
            .OrderBy(x => x.MetodoEntrega)
            .ThenBy(x => x.FechaModificacion)
            .ThenBy(x => x.Id)
            .ToListAsync();

        ventas = ventas
            .Where(x => x.EstadoVenta?.Nombre?.Contains("elimin", StringComparison.OrdinalIgnoreCase) != true)
            .Where(x => string.IsNullOrWhiteSpace(clientSearch) || (x.Cliente?.Nombre ?? string.Empty).Contains(clientSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var detalles = ventas.Select(x => new ReporteEnvioDetalleDto(
            x.Id,
            x.FechaModificacion,
            x.Cliente?.Nombre ?? string.Empty,
            x.MetodoEntrega,
            MetodoEntregaLabel(x.MetodoEntrega),
            x.EstadoVenta?.Nombre ?? x.EstadoVentaId,
            x.UsuarioEntregaPedido is null ? null : NombreUsuario(x.UsuarioEntregaPedido),
            x.Cliente?.DatosEnvio?.Ciudad?.Nombre ?? x.Cliente?.Ciudad?.Nombre,
            x.TotalVenta,
            string.Equals(x.MetodoEntrega, "TRANSPORTADORA", StringComparison.OrdinalIgnoreCase)
                ? x.MontoEnvioTransportadora
                : 0)).ToList();

        var resumen = ventas
            .GroupBy(x => new
            {
                x.UsuarioEntregaPedidoId,
                UsuarioEntrega = x.UsuarioEntregaPedido is null ? "Sin usuario entrega" : NombreUsuario(x.UsuarioEntregaPedido)
            })
            .Select(group =>
            {
                var transportadora = group
                    .Where(x => string.Equals(x.MetodoEntrega, "TRANSPORTADORA", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return new ReporteEnvioResumenDto(
                    group.Key.UsuarioEntregaPedidoId,
                    group.Key.UsuarioEntrega,
                    group.Count(),
                    transportadora.Count,
                    group.Sum(x => x.TotalVenta),
                    transportadora.Sum(x => x.MontoEnvioTransportadora));
            })
            .OrderBy(x => x.UsuarioEntrega)
            .ToList();

        return Ok(new ReporteEnviosDto(from, to, resumen, detalles));
    }

    [HttpGet("resumen-gerencial")]
    public async Task<ActionResult<ReporteResumenGerencialDto>> GetResumenGerencial(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .AsNoTracking()
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .Where(x => vendedorId == null || x.VendedorId == vendedorId.Value)
            .Where(x => x.EstadoVentaId != "XX")
            .OrderBy(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(EsVentaComisionable)
            .ToList();

        var ventasPerfilId = await ProfileIdAsync("Ventas");
        var ventaExternaPerfilId = await ProfileIdAsync("Venta Externa");
        var teamLeaderPerfilId = await ProfileIdAsync("Team Leader");
        var comisiones = await BuildComisionesAsync(from, to, vendedorId, perfilId: ventasPerfilId);
        var comisionesExternas = await BuildComisionesAsync(from, to, vendedorId, "externos", ventaExternaPerfilId);
        var comisionesTeamLeaders = await BuildComisionesAsync(from, to, vendedorId, "team-leaders", teamLeaderPerfilId);
        var pagosComisiones = await _context.LotesPago
            .AsNoTracking()
            .Where(x => x.TipoPago == TipoPagoComisiones)
            .Where(x => x.FechaPago >= from && x.FechaPago < toExclusive)
            .Where(x => vendedorId == null || x.VendedorId == vendedorId.Value)
            .Where(x => x.Estado == EstadoLotePagado)
            .ToListAsync();

        var totalVendido = ventas.Sum(x => x.TotalVenta);
        var cantidadPedidos = ventas.Count;
        var ventasPorProducto = ventas
            .SelectMany(venta => venta.Detalles.Select(detalle => new
            {
                PedidoId = venta.Id,
                Producto = detalle.Producto?.Nombre ?? "Sin producto",
                detalle.Cantidad,
                Total = detalle.PrecioTotal ?? (detalle.Cantidad * detalle.PrecioUnitario + (detalle.PrecioExtra ?? 0))
            }))
            .GroupBy(x => x.Producto)
            .Select(group => new ReporteResumenProductoDto(
                group.Key,
                group.Select(x => x.PedidoId).Distinct().Count(),
                group.Sum(x => x.Cantidad),
                group.Sum(x => x.Total)))
            .OrderByDescending(x => x.TotalVenta)
            .ThenBy(x => x.Producto)
            .ToList();
        var ventasPorMaquina = ventas
            .SelectMany(venta => venta.Detalles.Select(detalle => new
            {
                PedidoId = venta.Id,
                Maquina = detalle.TipoMaquina?.Nombre ?? "Sin maquina",
                detalle.Cantidad,
                Total = detalle.PrecioTotal ?? (detalle.Cantidad * detalle.PrecioUnitario + (detalle.PrecioExtra ?? 0))
            }))
            .GroupBy(x => x.Maquina)
            .Select(group => new ReporteResumenMaquinaDto(
                group.Key,
                group.Select(x => x.PedidoId).Distinct().Count(),
                group.Sum(x => x.Cantidad),
                group.Sum(x => x.Total)))
            .OrderByDescending(x => x.TotalVenta)
            .ThenBy(x => x.Maquina)
            .ToList();
        decimal PagadoPorPerfil(int perfilId) => perfilId <= 0
            ? 0
            : pagosComisiones.Where(x => x.PerfilId == perfilId).Sum(x => x.MontoTotal);

        var comisionVentas = BuildResumenPerfilComision(
            ventasPerfilId,
            "Ventas",
            comisiones,
            PagadoPorPerfil(ventasPerfilId));
        var comisionExternas = BuildResumenPerfilComision(
            ventaExternaPerfilId,
            "Venta externa",
            comisionesExternas,
            PagadoPorPerfil(ventaExternaPerfilId));
        var comisionTeamLeaders = BuildResumenPerfilComision(
            teamLeaderPerfilId,
            "Team Leader",
            comisionesTeamLeaders,
            PagadoPorPerfil(teamLeaderPerfilId));
        var totalComisionPagada = pagosComisiones.Sum(x => x.MontoTotal);
        var totalVendidoComisionPagada = totalVendido - totalComisionPagada;
        return Ok(new ReporteResumenGerencialDto(
            from,
            to,
            totalVendido,
            cantidadPedidos,
            cantidadPedidos == 0 ? 0 : totalVendido / cantidadPedidos,
            totalVendidoComisionPagada,
            totalComisionPagada,
            ventasPorProducto,
            ventasPorMaquina,
            new[] { comisionVentas, comisionExternas, comisionTeamLeaders }));
    }

    private static ReporteResumenPerfilComisionDto BuildResumenPerfilComision(
        int perfilId,
        string perfil,
        ReporteComisionesDto reporte,
        decimal totalPagado)
    {
        return new ReporteResumenPerfilComisionDto(
            perfilId,
            perfil,
            reporte.Vendedores.Sum(x => x.CantidadPedidos),
            reporte.Vendedores.Sum(x => x.TotalVenta),
            totalPagado);
    }

    private async Task<ReporteComisionesDto> BuildComisionesAsync(DateTime? dateFrom, DateTime? dateTo, int? vendedorId, string? scope = null, int? perfilId = null, int? vendedorExternoId = null)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);
        var scopeTeamLeaders = IsTeamLeaderCommissionsScope(scope);

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.EstadoVenta)
            .AsNoTracking()
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .Where(x => vendedorId == null || scopeTeamLeaders || perfilId != null || x.VendedorId == vendedorId.Value)
            .Where(x => !x.Reposicion)
            .Where(x => x.Detalles.Any(d => EstadosVentaComisionables.Contains(d.EstadoItem.Trim())))
            .OrderBy(x => x.VendedorId)
            .ThenBy(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(EsVentaComisionable)
            .ToList();

        var vendedores = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Persona is null ? x.NombreUsuario ?? $"Usuario {x.Id}" : NombrePersona(x.Persona));
        var perfilesUsuario = await _context.UsuarioPerfiles
            .Include(x => x.Perfil)
            .AsNoTracking()
            .Where(x => x.Estado && x.Perfil != null && x.Perfil.Estado)
            .Select(x => new { x.UsuarioId, x.PerfilId, Perfil = x.Perfil!.Nombre })
            .ToListAsync();
        var perfilesPorUsuario = perfilesUsuario
            .GroupBy(x => x.UsuarioId)
            .ToDictionary(x => x.Key, x => x.Select(item => item.PerfilId).ToList());
        var usuariosPerfilVendedor = perfilesUsuario
            .Where(x => IsSellerProfile(x.Perfil))
            .Select(x => x.UsuarioId)
            .Distinct()
            .ToHashSet();
        var perfilSeleccionado = perfilId.HasValue
            ? perfilesUsuario.FirstOrDefault(x => x.PerfilId == perfilId.Value)?.Perfil
            : null;
        scopeTeamLeaders = scopeTeamLeaders ||
            (perfilId.HasValue && perfilSeleccionado?.Contains("team leader", StringComparison.OrdinalIgnoreCase) == true);
        var usuariosPerfilSeleccionado = perfilId.HasValue
            ? perfilesUsuario.Where(x => x.PerfilId == perfilId.Value).Select(x => x.UsuarioId).ToHashSet()
            : new HashSet<int>();
        var comisiones = await _context.ProductoComisiones
            .AsNoTracking()
            .Where(x => x.Estado)
            .Where(x => x.FechaHasta == null || x.FechaHasta >= from)
            .Where(x => x.FechaDesde == null || x.FechaDesde < toExclusive)
            .ToListAsync();
        var teamLeadersPorVendedor = await _context.GrupoVentaVendedores
            .Include(x => x.GrupoVenta)
            .AsNoTracking()
            .Where(x => x.Estado && x.GrupoVenta.Estado)
            .ToDictionaryAsync(x => x.VendedorUsuarioId, x => x.GrupoVenta.TeamLeaderUsuarioId);
        var vendedoresExternos = teamLeadersPorVendedor.Keys.ToHashSet();
        var scopeExternos = IsExternalCommissionsScope(scope);

        ventas = ventas
            .Where(x => vendedorExternoId == null || x.VendedorId == vendedorExternoId.Value)
            .Where(x => scopeExternos || scopeTeamLeaders
                ? vendedoresExternos.Contains(x.VendedorId)
                : perfilId.HasValue
                    ? usuariosPerfilSeleccionado.Contains(x.VendedorId)
                    : usuariosPerfilVendedor.Contains(x.VendedorId) && !vendedoresExternos.Contains(x.VendedorId))
            .Where(x => !perfilId.HasValue || scopeTeamLeaders || scopeExternos || vendedorId == null || x.VendedorId == vendedorId.Value)
            .ToList();

        var detallesComision = new List<(int UsuarioId, ReporteComisionDetalleDto Detalle)>();
        if (scopeTeamLeaders || scopeExternos)
        {
            var commissionProfileId = perfilId ?? await ProfileIdAsync(scopeTeamLeaders ? "Team Leader" : "Venta externa");
            var vendedoresExternosNombres = vendedores;
            foreach (var venta in ventas)
            {
                if (!teamLeadersPorVendedor.TryGetValue(venta.VendedorId, out var teamLeaderId))
                {
                    continue;
                }

                if (vendedorId.HasValue && teamLeaderId != vendedorId.Value)
                {
                    continue;
                }

                foreach (var detalle in venta.Detalles.Where(EsDetalleComisionable))
                {
                    detallesComision.Add((teamLeaderId, BuildComisionDetallePorPerfil(
                        venta,
                        detalle,
                        commissionProfileId,
                        comisiones,
                        incluirExtra: scopeExternos,
                        vendedorOrigen: vendedoresExternosNombres.GetValueOrDefault(venta.VendedorId, $"Usuario {venta.VendedorId}"),
                        vendedorOrigenId: venta.VendedorId)));
                }
            }
        }
        else
        {
            foreach (var venta in ventas)
            {
                foreach (var detalle in venta.Detalles.Where(EsDetalleComisionable))
                {
                    var detalleComision = perfilId.HasValue
                        ? BuildComisionDetallePorPerfil(venta, detalle, perfilId.Value, comisiones, incluirExtra: true)
                        : BuildComisionDetalle(venta, detalle, venta.VendedorId, perfilesPorUsuario, comisiones, incluirExtra: true);
                    detallesComision.Add((venta.VendedorId, detalleComision));
                }
            }
        }

        var grouped = detallesComision
            .GroupBy(x => x.UsuarioId)
            .Select(group =>
            {
                var detalles = group.Select(x => x.Detalle).ToList();
                var pedidoIds = detalles.Select(x => x.PedidoId).Distinct().ToHashSet();

                return new ReporteComisionVendedorDto(
                    group.Key,
                    vendedores.GetValueOrDefault(group.Key, $"Usuario {group.Key}"),
                    pedidoIds.Count,
                    ventas.Where(x => pedidoIds.Contains(x.Id)).Sum(x => x.TotalVenta),
                    detalles.Sum(x => x.ComisionTotal),
                    detalles);
            })
            .OrderByDescending(x => x.TotalComision)
            .ThenBy(x => x.Vendedor)
            .ToList();

        return new ReporteComisionesDto(from, to, grouped);
    }

    private static bool IsExternalCommissionsScope(string? scope)
    {
        return scope is not null &&
            (scope.Equals("externos", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("external", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("vendedores-externos", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTeamLeaderCommissionsScope(string? scope)
    {
        return scope is not null &&
            (scope.Equals("team-leaders", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("teamleader", StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("team-leader", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSellerProfile(string? profile)
    {
        return profile is not null &&
            (profile.Contains("vendedor", StringComparison.OrdinalIgnoreCase) ||
             profile.Contains("ventas", StringComparison.OrdinalIgnoreCase));
    }

    private static bool EsVentaComisionable(VentaImpresionCab venta)
    {
        var estadoId = (venta.EstadoVentaId ?? string.Empty).Trim();
        var estado = venta.EstadoVenta?.Nombre ?? string.Empty;
        return estadoId is not ("XX" or "RE" or "PC") &&
            !estado.Contains("elimin", StringComparison.OrdinalIgnoreCase) &&
            !estado.Contains("rechaz", StringComparison.OrdinalIgnoreCase) &&
            !estado.Contains("carga", StringComparison.OrdinalIgnoreCase) &&
            venta.Detalles.Any(EsDetalleComisionable);
    }

    private static bool EsDetalleComisionable(VentaImpresionDet detalle)
    {
        return EstadosVentaComisionables.Contains((detalle.EstadoItem ?? string.Empty).Trim());
    }
    private static ReporteComisionDetalleDto BuildComisionDetalle(
        VentaImpresionCab venta,
        VentaImpresionDet detalle,
        int usuarioComisionId,
        IReadOnlyDictionary<int, List<int>> perfilesPorUsuario,
        IReadOnlyCollection<ProductoComision> comisiones,
        bool incluirExtra)
    {
        var precioExtra = detalle.PrecioExtra ?? 0;
        var totalDetalle = detalle.PrecioTotal ?? (detalle.Cantidad * detalle.PrecioUnitario + precioExtra);
        var comisionUnitario = ResolveComision(detalle.ProductoId, usuarioComisionId, venta.FechaCreacion, perfilesPorUsuario, comisiones);
        var comisionTotal = detalle.Cantidad * comisionUnitario + (incluirExtra ? precioExtra : 0);

        return new ReporteComisionDetalleDto(
            venta.Id,
            venta.FechaCreacion,
            venta.Cliente?.Nombre ?? string.Empty,
            detalle.Producto?.Nombre ?? $"Producto {detalle.ProductoId}",
            detalle.Cantidad,
            detalle.PrecioUnitario,
            incluirExtra ? precioExtra : 0,
            totalDetalle,
            comisionUnitario,
            comisionTotal);
    }

    private static ReporteComisionDetalleDto BuildComisionDetallePorPerfil(
        VentaImpresionCab venta,
        VentaImpresionDet detalle,
        int perfilComisionId,
        IReadOnlyCollection<ProductoComision> comisiones,
        bool incluirExtra,
        string? vendedorOrigen = null,
        int? vendedorOrigenId = null)
    {
        var precioExtra = detalle.PrecioExtra ?? 0;
        var totalDetalle = detalle.PrecioTotal ?? (detalle.Cantidad * detalle.PrecioUnitario + precioExtra);
        var comisionUnitario = ResolveComisionPorPerfil(detalle.ProductoId, perfilComisionId, venta.FechaCreacion, comisiones);
        var comisionTotal = detalle.Cantidad * comisionUnitario + (incluirExtra ? precioExtra : 0);

        return new ReporteComisionDetalleDto(
            venta.Id,
            venta.FechaCreacion,
            venta.Cliente?.Nombre ?? string.Empty,
            detalle.Producto?.Nombre ?? $"Producto {detalle.ProductoId}",
            detalle.Cantidad,
            detalle.PrecioUnitario,
            incluirExtra ? precioExtra : 0,
            totalDetalle,
            comisionUnitario,
            comisionTotal,
            vendedorOrigen,
            vendedorOrigenId);
    }

    private static decimal ResolveComision(
        int productoId,
        int usuarioId,
        DateTime fecha,
        IReadOnlyDictionary<int, List<int>> perfilesPorUsuario,
        IReadOnlyCollection<ProductoComision> comisiones)
    {
        if (!perfilesPorUsuario.TryGetValue(usuarioId, out var perfilIds))
        {
            return 0;
        }

        var fechaVenta = fecha.Date;
        return comisiones
            .Where(x => x.ProductoId == productoId && perfilIds.Contains(x.PerfilId))
            .Where(x => x.FechaDesde == null || x.FechaDesde.Value.Date <= fechaVenta)
            .Where(x => x.FechaHasta == null || x.FechaHasta.Value.Date >= fechaVenta)
            .OrderByDescending(x => x.FechaDesde ?? DateTime.MinValue)
            .Select(x => x.MontoPorMetro)
            .FirstOrDefault();
    }

    private static decimal ResolveComisionPorPerfil(
        int productoId,
        int perfilId,
        DateTime fecha,
        IReadOnlyCollection<ProductoComision> comisiones)
    {
        if (perfilId <= 0)
        {
            return 0;
        }

        var fechaVenta = fecha.Date;
        return comisiones
            .Where(x => x.ProductoId == productoId && x.PerfilId == perfilId)
            .Where(x => x.FechaDesde == null || x.FechaDesde.Value.Date <= fechaVenta)
            .Where(x => x.FechaHasta == null || x.FechaHasta.Value.Date >= fechaVenta)
            .OrderByDescending(x => x.FechaDesde ?? DateTime.MinValue)
            .Select(x => x.MontoPorMetro)
            .FirstOrDefault();
    }

    private static byte[] BuildComisionesXlsx(ReporteComisionesDto report)
    {
        var rows = new List<string[]>
        {
            new[] { "Vendedor", "Pedido", "Fecha", "Cliente", "Producto", "Cantidad", "Precio unitario", "Precio extra", "Total detalle", "Comision unitario", "Comision total" }
        };

        foreach (var seller in report.Vendedores)
        {
            rows.Add(new[]
            {
                seller.Vendedor,
                string.Empty,
                string.Empty,
                $"Pedidos: {seller.CantidadPedidos}",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Money(seller.TotalVenta),
                string.Empty,
                Money(seller.TotalComision)
            });

            rows.AddRange(seller.Detalles.Select(detail => new[]
            {
                seller.Vendedor,
                detail.PedidoId.ToString(CultureInfo.InvariantCulture),
                detail.Fecha.ToString("yyyy-MM-dd"),
                detail.Cliente,
                detail.Producto,
                detail.Cantidad.ToString("N2", CultureInfo.CurrentCulture),
                Money(detail.PrecioUnitario),
                Money(detail.PrecioExtra),
                Money(detail.TotalDetalle),
                Money(detail.ComisionUnitario),
                Money(detail.ComisionTotal)
            }));
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                    <Default Extension="xml" ContentType="application/xml"/>
                    <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                    <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                    <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            AddZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            AddZipEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                    <sheets><sheet name="Comisiones" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                    <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            AddZipEntry(archive, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                    <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                    <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
                    <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                    <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                    <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
                    <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                </styleSheet>
                """);
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows));
        }

        return stream.ToArray();
    }

    private async Task<LotePago> GetOrCreateComisionesLoteAsync(
        ReporteComisionesDto report,
        int? vendedorId,
        string? scope,
        int? perfilId)
    {
        var effectivePerfilId = perfilId;
        if (!effectivePerfilId.HasValue && IsExternalCommissionsScope(scope))
        {
            effectivePerfilId = await ProfileIdAsync("Venta Externa");
        }
        else if (!effectivePerfilId.HasValue && IsTeamLeaderCommissionsScope(scope))
        {
            effectivePerfilId = await ProfileIdAsync("Team Leader");
        }

        var scopeFilePart = IsExternalCommissionsScope(scope)
            ? "externos-team-leaders"
            : IsTeamLeaderCommissionsScope(scope)
                ? "team-leaders"
                : "vendedores";
        var profileFilePart = effectivePerfilId.HasValue ? $"perfil-{effectivePerfilId.Value}" : "todos-los-perfiles";
        var concepto = await ResolveCommissionPaymentConceptAsync(scope, effectivePerfilId);
        var paymentLogicVersion = IsExternalCommissionsScope(scope) ? "team-leader-estados-v4" : "estados-v4";
        var lotFilePrefix = $"comisiones-{scopeFilePart}-{profileFilePart}-{paymentLogicVersion}-";
        var existing = await _context.LotesPago
            .AsNoTracking()
            .Where(x => x.TipoPago == TipoPagoComisiones)
            .Where(x => x.FechaDesde == report.FechaDesde.Date && x.FechaHasta == report.FechaHasta.Date)
            .Where(x => x.FechaPago == report.FechaHasta.Date)
            .Where(x => x.VendedorId == vendedorId)
            .Where(x => x.PerfilId == effectivePerfilId)
            .Where(x => x.NombreArchivo.Contains(lotFilePrefix))
            .Where(x => x.Estado != EstadoLoteAnulado)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return existing;
        }

        var file = await BuildComisionesBancoTxtAsync(report, report.FechaHasta.Date, scope, concepto);
        var sellerFilePart = await ReportFileSellerNameAsync(vendedorId, report);
        var lote = new LotePago
        {
            TipoPago = TipoPagoComisiones,
            FechaGeneracion = DateTime.Now,
            UsuarioGeneroId = CurrentUserId() ?? 1,
            FechaDesde = report.FechaDesde.Date,
            FechaHasta = report.FechaHasta.Date,
            FechaPago = report.FechaHasta.Date,
            VendedorId = vendedorId,
            PerfilId = effectivePerfilId,
            MontoTotal = file.Rows.Sum(x => x.Monto),
            CantidadPersonas = file.Rows.Count,
            NombreArchivo = $"banco-continental-{lotFilePrefix}{sellerFilePart}-{DateTime.Now:yyyyMMddHHmm}.txt",
            Estado = EstadoLoteGenerado,
            ContenidoTxt = file.Content
        };

        foreach (var row in file.Rows)
        {
            lote.Detalles.Add(new LotePagoDetalle
            {
                UsuarioId = row.UsuarioId,
                Vendedor = row.Vendedor,
                Documento = row.Documento,
                CuentaDebitoEmpresa = row.CuentaDebitoEmpresa,
                Concepto = row.Concepto,
                Monto = row.Monto,
                EsAguinaldo = row.EsAguinaldo,
                FechaPago = row.FechaPago,
                TipoCuenta = row.TipoCuenta,
                LineaTxt = row.LineaTxt
            });
        }

        _context.LotesPago.Add(lote);
        await _context.SaveChangesAsync();
        return lote;
    }

    private async Task<BankTxtFile> BuildComisionesBancoTxtAsync(
        ReporteComisionesDto report,
        DateTime fechaPago,
        string? scope,
        string concepto)
    {
        var cuentaDebitoEmpresa = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 1, "012312345699");
        var esAguinaldo = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 3, "NO");
        var tipoCuenta = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 4, "CC");

        if (string.IsNullOrWhiteSpace(cuentaDebitoEmpresa))
        {
            throw new InvalidOperationException("Falta configurar la cuenta de debito de la empresa para Banco Continental.");
        }

        var payees = await BuildCommissionTxtPayeesAsync(report, scope);
        var sellerIds = payees.Select(x => x.UsuarioId).Distinct().ToList();

        var sellers = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .Where(x => sellerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var rows = new List<BankTxtRow>();
        var missing = new List<string>();

        foreach (var seller in payees.OrderBy(x => x.Vendedor))
        {
            sellers.TryGetValue(seller.UsuarioId, out var usuario);
            var documento = usuario?.Persona?.Documento?.Trim();
            if (string.IsNullOrWhiteSpace(documento))
            {
                missing.Add(seller.Vendedor);
                continue;
            }

            var line = string.Join(",",
                Quote(documento),
                Quote(cuentaDebitoEmpresa.Trim()),
                Quote(concepto.Trim()),
                Quote(seller.TotalComision.ToString("0.00", CultureInfo.InvariantCulture)),
                Quote(esAguinaldo.Trim().ToUpperInvariant() == "SI" ? "SI" : "NO"),
                Quote(string.Empty),
                Quote(fechaPago.ToString("dd/MM/yyyy")),
                Quote(NormalizeTipoCuenta(tipoCuenta)));

            rows.Add(new BankTxtRow(
                seller.UsuarioId,
                seller.Vendedor,
                documento,
                cuentaDebitoEmpresa.Trim(),
                concepto.Trim(),
                seller.TotalComision,
                esAguinaldo.Trim().ToUpperInvariant() == "SI" ? "SI" : "NO",
                fechaPago,
                NormalizeTipoCuenta(tipoCuenta),
                line));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Falta cargar documento para generar el TXT del banco: {string.Join(", ", missing)}.");
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No hay comisiones con monto mayor a cero para generar el TXT del banco.");
        }

        return new BankTxtFile(string.Join(Environment.NewLine, rows.Select(x => x.LineaTxt)), rows);
    }

    private async Task<string> ResolveCommissionPaymentConceptAsync(string? scope, int? perfilId)
    {
        if (IsExternalCommissionsScope(scope))
        {
            return "PAGO COMISION VENDEDOR EXTERNO";
        }

        if (IsTeamLeaderCommissionsScope(scope))
        {
            return "PAGO DE COMISION TEAM LEADER";
        }

        if (!perfilId.HasValue)
        {
            return "PAGO DE COMISION";
        }

        var profileName = await _context.Perfiles
            .AsNoTracking()
            .Where(x => x.Id == perfilId.Value)
            .Select(x => x.Nombre)
            .FirstOrDefaultAsync() ?? string.Empty;

        if (profileName.Contains("extern", StringComparison.OrdinalIgnoreCase))
        {
            return "PAGO COMISION VENDEDOR EXTERNO";
        }

        return profileName.Contains("team leader", StringComparison.OrdinalIgnoreCase)
            ? "PAGO DE COMISION TEAM LEADER"
            : "PAGO DE COMISION";
    }
    private async Task<List<CommissionTxtPayee>> BuildCommissionTxtPayeesAsync(ReporteComisionesDto report, string? scope)
    {
        var sellers = report.Vendedores
            .Where(x => x.TotalComision > 0)
            .ToList();

        if (!IsExternalCommissionsScope(scope))
        {
            return sellers
                .Select(x => new CommissionTxtPayee(x.VendedorId, x.Vendedor, x.TotalComision))
                .ToList();
        }

        var isGroupedByTeamLeader = sellers.Count > 0 &&
            sellers.All(x => x.Detalles.Any() &&
                x.Detalles.All(detail => !string.IsNullOrWhiteSpace(detail.VendedorOrigen)));
        if (isGroupedByTeamLeader)
        {
            var reportTeamLeaderIds = sellers.Select(x => x.VendedorId).Distinct().ToList();
            var activeTeamLeaderIds = await _context.GrupoVentaVendedores
                .Include(x => x.GrupoVenta)
                .AsNoTracking()
                .Where(x => x.Estado && x.GrupoVenta.Estado &&
                    reportTeamLeaderIds.Contains(x.GrupoVenta.TeamLeaderUsuarioId))
                .Select(x => x.GrupoVenta.TeamLeaderUsuarioId)
                .Distinct()
                .ToListAsync();
            var invalidTeamLeaders = sellers
                .Where(x => !activeTeamLeaderIds.Contains(x.VendedorId))
                .Select(x => x.Vendedor)
                .ToList();

            if (invalidTeamLeaders.Count > 0)
            {
                throw new InvalidOperationException(
                    $"El TXT externo solo puede pagar a Team Leaders activos: {string.Join(", ", invalidTeamLeaders)}.");
            }

            return sellers
                .Select(x => new CommissionTxtPayee(x.VendedorId, x.Vendedor, x.TotalComision))
                .ToList();
        }

        var sellerIds = sellers.Select(x => x.VendedorId).Distinct().ToList();
        var groupSellerRows = await _context.GrupoVentaVendedores
            .Include(x => x.GrupoVenta)
            .AsNoTracking()
            .Where(x => x.Estado && x.GrupoVenta.Estado && sellerIds.Contains(x.VendedorUsuarioId))
            .ToListAsync();
        var teamLeadersPorVendedor = groupSellerRows
            .GroupBy(x => x.VendedorUsuarioId)
            .ToDictionary(x => x.Key, x => x.OrderBy(item => item.Id).First().GrupoVenta.TeamLeaderUsuarioId);
        var sellersWithoutTeamLeader = sellers
            .Where(x => !teamLeadersPorVendedor.ContainsKey(x.VendedorId))
            .Select(x => x.Vendedor)
            .ToList();

        if (sellersWithoutTeamLeader.Count > 0)
        {
            throw new InvalidOperationException($"Falta configurar Team Leader para generar el TXT externo: {string.Join(", ", sellersWithoutTeamLeader)}.");
        }

        var teamLeaderIds = teamLeadersPorVendedor.Values.Distinct().ToList();
        var teamLeaderNames = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .Where(x => teamLeaderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Persona is null ? x.NombreUsuario ?? $"Usuario {x.Id}" : NombrePersona(x.Persona));

        return sellers
            .GroupBy(x => teamLeadersPorVendedor[x.VendedorId])
            .Select(group => new CommissionTxtPayee(
                group.Key,
                teamLeaderNames.GetValueOrDefault(group.Key, $"Usuario {group.Key}"),
                group.Sum(x => x.TotalComision)))
            .ToList();
    }

    private async Task<string> ConfigValueAsync(string nombre, int nroConfiguracion, string defaultValue)
    {
        var value = await _context.Configuraciones
            .AsNoTracking()
            .Where(x => x.Nombre == nombre && x.NroConfiguracion == nroConfiguracion)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string NormalizeTipoCuenta(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized == "AHO" ? "AHO" : "CC";
    }

    private static string NormalizeLoteEstado(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals(EstadoLotePagado, StringComparison.OrdinalIgnoreCase)
            ? EstadoLotePagado
            : normalized.Equals(EstadoLoteAnulado, StringComparison.OrdinalIgnoreCase)
                ? EstadoLoteAnulado
                : EstadoLoteGenerado;
    }

    private static string Quote(string value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private async Task<int> ProfileIdAsync(string profileName)
    {
        return await _context.Perfiles
            .AsNoTracking()
            .Where(x => x.Estado && x.Nombre == profileName)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();
    }

    private static LotePagoDto ToLotePagoDto(LotePago lote)
    {
        return new LotePagoDto(
            lote.Id,
            lote.TipoPago,
            lote.FechaGeneracion,
            lote.UsuarioGenero is null ? $"Usuario {lote.UsuarioGeneroId}" : NombreUsuario(lote.UsuarioGenero),
            lote.FechaDesde,
            lote.FechaHasta,
            lote.FechaPago,
            lote.Vendedor is null ? null : NombreUsuario(lote.Vendedor),
            lote.PerfilId,
            lote.Perfil?.Nombre,
            lote.MontoTotal,
            lote.CantidadPersonas,
            lote.NombreArchivo,
            lote.Estado);
    }

    private static string NombreUsuario(Usuario usuario)
    {
        return usuario.Persona is null ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}" : NombrePersona(usuario.Persona);
    }

    private static string MetodoEntregaLabel(string? method)
    {
        return (method ?? "DELIVERY").ToUpperInvariant() switch
        {
            "TRANSPORTADORA" => "Transportadora",
            "MOTOBOLT" => "Motobolt",
            "RETIRO_LOCAL" => "Retiro del local",
            "OTRO" => "Otro",
            _ => "Delivery"
        };
    }

    private record CommissionTxtPayee(int UsuarioId, string Vendedor, decimal TotalComision);

    private record BankTxtFile(string Content, IReadOnlyList<BankTxtRow> Rows);

    private record BankTxtRow(
        int UsuarioId,
        string Vendedor,
        string Documento,
        string CuentaDebitoEmpresa,
        string Concepto,
        decimal Monto,
        string EsAguinaldo,
        DateTime FechaPago,
        string TipoCuenta,
        string LineaTxt);

    private static string BuildWorksheetXml(IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append("<row r=\"").Append(rowIndex + 1).AppendLine("\">");

            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                builder.Append("<c r=\"")
                    .Append(CellReference(columnIndex, rowIndex + 1))
                    .Append("\" t=\"inlineStr\"");

                if (rowIndex == 0)
                {
                    builder.Append(" s=\"1\"");
                }

                builder.Append("><is><t>")
                    .Append(WebUtility.HtmlEncode(rows[rowIndex][columnIndex] ?? string.Empty))
                    .AppendLine("</t></is></c>");
            }

            builder.AppendLine("</row>");
        }

        builder.AppendLine("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string CellReference(int columnIndex, int rowIndex)
    {
        var dividend = columnIndex + 1;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return $"{columnName}{rowIndex}";
    }

    private static string NombrePersona(Persona persona)
    {
        var parts = new[] { persona.PrimerNombre, persona.SegundoNombre, persona.PrimerApellido, persona.SegundoApellido }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var nombre = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(nombre) ? $"Persona {persona.Id}" : nombre;
    }

    private static string Money(decimal value)
    {
        return $"Gs. {value:N0}";
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private async Task<string> ReportFileSellerNameAsync(int? vendedorId, ReporteComisionesDto report)
    {
        if (vendedorId.HasValue)
        {
            var sellerName = await _context.Usuarios
                .AsNoTracking()
                .Where(x => x.Id == vendedorId.Value)
                .Select(x => x.Persona == null
                    ? x.NombreUsuario
                    : (x.Persona.PrimerNombre ?? string.Empty) + " " + (x.Persona.PrimerApellido ?? string.Empty))
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(sellerName))
            {
                return SafeFilePart(sellerName);
            }
        }

        var seller = report.Vendedores.Count() == 1 ? FirstNameFirstSurname(report.Vendedores.First().Vendedor) : "todos";

        return SafeFilePart(seller);
    }

    private static string FirstNameFirstSurname(string value)
    {
        var parts = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 3)
        {
            return $"{parts[0]} {parts[2]}";
        }

        return parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : value;
    }

    private static string SafeFilePart(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousDash = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } safe ? safe : "vendedor";
    }

    private static class CommissionPdfBuilder
    {
        private const decimal PageWidth = 842;
        private const decimal PageHeight = 595;
        private const decimal MarginX = 40;
        private const decimal TealR = 0.18m;
        private const decimal TealG = 0.53m;
        private const decimal TealB = 0.61m;
        private const decimal LightR = 0.82m;
        private const decimal LightG = 0.93m;
        private const decimal LightB = 0.96m;
        private const decimal BorderR = 0.72m;
        private const decimal BorderG = 0.82m;
        private const decimal BorderB = 0.88m;

        public static byte[] Build(
            ReporteComisionesDto report,
            string? webRootPath,
            bool isTeamLeaderReport = false,
            bool isExternalSellerDetail = false)
        {
            var pages = new List<string>();
            var logo = PdfPngImage.TryLoad(GetLogoPath(webRootPath));

            foreach (var seller in report.Vendedores.DefaultIfEmpty(new ReporteComisionVendedorDto(0, "Sin ventas", 0, 0, 0, Array.Empty<ReporteComisionDetalleDto>())))
            {
                var writer = new PdfPageWriter();
                DrawSellerReport(writer, report, seller, logo, isTeamLeaderReport, isExternalSellerDetail);
                pages.AddRange(writer.Pages);
            }

            return WriteDocument(pages, logo);
        }

        public static byte[] BuildBudget(VentaImpresionCab pedido, string vendedor, string? webRootPath)
        {
            var writer = new PdfPageWriter();
            var logo = PdfPngImage.TryLoad(GetLogoPath(webRootPath));
            DrawBudget(writer, pedido, vendedor, logo);
            return WriteDocument(writer.Pages.ToList(), logo);
        }

        private static void DrawBudget(PdfPageWriter writer, VentaImpresionCab pedido, string vendedor, PdfPngImage? logo)
        {
            const decimal darkR = 0.05m;
            const decimal darkG = 0.18m;
            const decimal darkB = 0.24m;
            const decimal mutedR = 0.34m;
            const decimal mutedG = 0.44m;
            const decimal mutedB = 0.49m;
            var pageContentWidth = PageWidth - MarginX * 2;
            var y = PageHeight - 24;

            if (logo is not null)
            {
                var logoWidth = 104m;
                var logoHeight = logoWidth * logo.Height / logo.Width;
                writer.Image("Im1", MarginX + 8, y - 53, logoWidth, logoHeight);
            }

            writer.TextCenter(PageWidth / 2, y - 23, "PRESUPUESTO", 21, bold: true, TealR, TealG, TealB);
            writer.TextCenter(PageWidth / 2, y - 39, "Propuesta comercial", 9, bold: false, mutedR, mutedG, mutedB);
            writer.Text(PageWidth - MarginX - 126, y - 21, $"PEDIDO #{pedido.Id}", 12, bold: true, darkR, darkG, darkB);
            writer.Text(PageWidth - MarginX - 126, y - 39, $"Emitido: {DateTime.Now:dd/MM/yyyy}", 9, bold: false, mutedR, mutedG, mutedB);

            y -= 74;
            var infoGap = 12m;
            var infoWidth = (pageContentWidth - infoGap) / 2;
            DrawInfoCard(
                writer,
                MarginX,
                y,
                infoWidth,
                "DATOS DEL CLIENTE",
                pedido.Cliente?.Nombre ?? "Sin cliente",
                $"Telefono: {pedido.Cliente?.NroTelefono ?? "Sin telefono"}",
                darkR, darkG, darkB);
            DrawInfoCard(
                writer,
                MarginX + infoWidth + infoGap,
                y,
                infoWidth,
                "INFORMACION DEL PEDIDO",
                $"Vendedor: {vendedor}",
                $"Fecha de entrega: {(pedido.FechaEntrega.HasValue ? pedido.FechaEntrega.Value.ToString("dd/MM/yyyy") : "Sin fecha")}",
                darkR, darkG, darkB);

            y -= 90;
            writer.Text(MarginX, y, "DETALLE DEL TRABAJO", 11, bold: true, TealR, TealG, TealB);
            writer.Text(PageWidth - MarginX - 150, y, $"{pedido.Detalles.Count} item(s)", 9, bold: false, mutedR, mutedG, mutedB);
            y -= 10;

            var widths = new[] { 34m, 250m, 116m, 70m, 100m, 90m, 102m };
            DrawBudgetTableRow(writer, y, widths,
                new[] { "#", "PRODUCTO / TRABAJO", "MAQUINA", "CANT.", "PRECIO UNIT.", "EXTRA", "TOTAL" },
                header: true);
            y -= 22;

            var index = 1;
            foreach (var detalle in pedido.Detalles.OrderBy(x => x.Id))
            {
                var totalDetalle = detalle.Cantidad * detalle.PrecioUnitario + (detalle.PrecioExtra ?? 0);
                DrawBudgetTableRow(writer, y, widths, new[]
                {
                    index.ToString(CultureInfo.InvariantCulture),
                    Trim(detalle.Producto?.Nombre ?? $"Producto {detalle.ProductoId}", 44),
                    Trim(detalle.TipoMaquina?.Nombre ?? "-", 19),
                    Quantity(detalle.Cantidad),
                    Money(detalle.PrecioUnitario),
                    Money(detalle.PrecioExtra ?? 0),
                    Money(totalDetalle)
                }, alternate: index % 2 == 0);
                y -= 22;
                index++;
            }

            if (pedido.MontoEnvioTransportadora > 0)
            {
                DrawBudgetTableRow(writer, y, widths,
                    new[] { "", "SERVICIO DE ENVIO", "", "", "", "", Money(pedido.MontoEnvioTransportadora) },
                    subtotal: true);
                y -= 22;
            }

            y -= 20;
            const decimal totalLabelWidth = 178;
            const decimal totalAmountWidth = 184;
            var totalX = PageWidth - MarginX - totalLabelWidth - totalAmountWidth;
            writer.Cell(totalX, y, totalLabelWidth, 36, "TOTAL DEL TRABAJO", 11, bold: true, center: true,
                fill: (0.88m, 0.95m, 0.96m), text: (TealR, TealG, TealB));
            writer.Cell(totalX + totalLabelWidth, y, totalAmountWidth, 36, Money(pedido.TotalVenta), 18, bold: true, center: true,
                fill: (TealR, TealG, TealB), text: (1, 1, 1));

            y -= 62;
            writer.Text(MarginX, y, "CONDICIONES Y OBSERVACIONES", 10, bold: true, darkR, darkG, darkB);
            y -= 8;
            var payment = pedido.FormaPago?.Nombre ?? pedido.FormaPagoId;
            var delivery = string.IsNullOrWhiteSpace(pedido.MetodoEntrega) ? "Sin especificar" : pedido.MetodoEntrega.Replace('_', ' ');
            writer.Cell(MarginX, y, pageContentWidth / 2, 24,
                $"Forma de pago: {Trim(payment, 34)}", 9, bold: true, center: false,
                fill: (0.96m, 0.98m, 0.99m), text: (darkR, darkG, darkB));
            writer.Cell(MarginX + pageContentWidth / 2, y, pageContentWidth / 2, 24,
                $"Entrega: {Trim(delivery, 34)}", 9, bold: true, center: false,
                fill: (0.96m, 0.98m, 0.99m), text: (darkR, darkG, darkB));
            y -= 24;
            var note = string.IsNullOrWhiteSpace(pedido.Observacion)
                ? "Presupuesto sujeto a confirmacion. Los importes corresponden a los productos y cantidades detallados."
                : $"Observacion: {pedido.Observacion}";
            writer.Cell(MarginX, y, pageContentWidth, 28, Trim(note, 125), 9, bold: false, center: false,
                fill: (0.96m, 0.93m, 0.99m), text: (0.25m, 0.20m, 0.35m));

            writer.Text(MarginX, 25, "EVALU - Presupuesto para cliente", 8, bold: true, TealR, TealG, TealB);
            writer.Text(PageWidth - MarginX - 155, 25, $"Documento generado el {DateTime.Now:dd/MM/yyyy HH:mm}", 8, bold: false, mutedR, mutedG, mutedB);
        }

        private static void DrawInfoCard(
            PdfPageWriter writer,
            decimal x,
            decimal y,
            decimal width,
            string title,
            string primary,
            string secondary,
            decimal darkR,
            decimal darkG,
            decimal darkB)
        {
            writer.Cell(x, y, width, 22, title, 8, bold: true, center: false,
                fill: (TealR, TealG, TealB), text: (1, 1, 1));
            writer.Cell(x, y - 22, width, 25, Trim(primary, 54), 11, bold: true, center: false,
                fill: (0.98m, 0.99m, 1m), text: (darkR, darkG, darkB));
            writer.Cell(x, y - 47, width, 22, Trim(secondary, 62), 9, bold: false, center: false,
                fill: (0.94m, 0.97m, 0.98m), text: (0.30m, 0.40m, 0.45m));
        }

        private static void DrawBudgetTableRow(
            PdfPageWriter writer,
            decimal y,
            IReadOnlyList<decimal> widths,
            IReadOnlyList<string> values,
            bool header = false,
            bool subtotal = false,
            bool alternate = false)
        {
            var x = MarginX;
            for (var i = 0; i < widths.Count; i++)
            {
                var fill = header
                    ? (TealR, TealG, TealB)
                    : subtotal
                        ? (0.88m, 0.95m, 0.96m)
                        : alternate
                            ? (0.95m, 0.98m, 0.99m)
                            : (0.99m, 0.995m, 1m);
                var text = header ? (1m, 1m, 1m) : (0.03m, 0.15m, 0.20m);
                writer.Cell(x, y, widths[i], 22, values[i], header ? 7 : 8,
                    bold: header || subtotal || i == widths.Count - 1,
                    center: header || i is 0 or 2,
                    right: !header && i >= 3,
                    fill: fill,
                    text: text);
                x += widths[i];
            }
        }
        private static string GetLogoPath(string? webRootPath)
        {
            var path = !string.IsNullOrWhiteSpace(webRootPath)
                ? Path.Combine(webRootPath, "assets", "report-logo.png")
                : string.Empty;

            return System.IO.File.Exists(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "report-logo.png");
        }

        private static void DrawSellerReport(
            PdfPageWriter writer,
            ReporteComisionesDto report,
            ReporteComisionVendedorDto seller,
            PdfPngImage? logo,
            bool isTeamLeaderReport,
            bool isExternalSellerDetail)
        {
            var y = PageHeight - 32;
            if (logo is not null)
            {
                var logoWidth = 96m;
                var logoHeight = logoWidth * logo.Height / logo.Width;
                writer.Image("Im1", MarginX, y - logoHeight, logoWidth, logoHeight);
            }

            var reportTitle = isExternalSellerDetail
                ? "REPORTE DE COMISIONES VENDEDOR EXTERNO"
                : isTeamLeaderReport
                    ? "REPORTE DE COMISIONES TEAM LEADER"
                    : "REPORTE DE COMISIONES POR VENDEDOR";
            writer.TextCenter(PageWidth / 2, y - 28, reportTitle, 17, bold: true, TealR, TealG, TealB);

            y -= 58;
            DrawSummaryTable(writer, y, report, seller, isTeamLeaderReport, isExternalSellerDetail);
            y -= 66;

            var groups = seller.Detalles
                .GroupBy(detail => detail.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                writer.Text(MarginX + 10, y, "No hay ventas para el rango seleccionado.", 11, bold: true, 0.1m, 0.24m, 0.32m);
                return;
            }

            foreach (var group in groups)
            {
                var requiredHeight = 38 + (group.Count() + 2) * 13;
                if (y - requiredHeight < 40)
                {
                    writer.NewPage();
                    y = PageHeight - 58;
                }

                y = DrawProductGroup(writer, y, group.Key, group.First().ComisionUnitario, group, isTeamLeaderReport);
                y -= 20;
            }
        }

        private static void DrawSummaryTable(
            PdfPageWriter writer,
            decimal yTop,
            ReporteComisionesDto report,
            ReporteComisionVendedorDto seller,
            bool isTeamLeaderReport,
            bool isExternalSellerDetail)
        {
            var x = MarginX;
            var totalWidth = PageWidth - MarginX * 2;
            var colWidth = totalWidth / 4;
            var headerHeight = 24;
            var valueHeight = 28;
            var externalSellerName = seller.Detalles
                .Select(detail => detail.VendedorOrigen)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            var headers = new[] { isExternalSellerDetail ? "Vendedor externo" : isTeamLeaderReport ? "Team Leader" : "Vendedor", "Rango de fecha", "Total vendido", "Total comision" };
            var values = new[]
            {
                Trim(isExternalSellerDetail ? externalSellerName ?? seller.Vendedor : seller.Vendedor, 30),
                $"{report.FechaDesde:dd/MM/yyyy}   al   {report.FechaHasta:dd/MM/yyyy}",
                Money(seller.TotalVenta),
                Money(seller.TotalComision)
            };

            for (var i = 0; i < headers.Length; i++)
            {
                writer.Cell(x + colWidth * i, yTop, colWidth, headerHeight, headers[i], 9, bold: true, center: true, fill: (TealR, TealG, TealB), text: (1, 1, 1));
                writer.Cell(x + colWidth * i, yTop - headerHeight, colWidth, valueHeight, values[i], 10, bold: true, center: true, fill: (0.93m, 0.97m, 0.99m), text: (0, 0, 0));
            }
        }

        private static decimal DrawProductGroup(
            PdfPageWriter writer,
            decimal y,
            string product,
            decimal commissionUnit,
            IGrouping<dynamic, ReporteComisionDetalleDto> group,
            bool isTeamLeaderReport)
        {
            writer.Text(MarginX, y, $"Detalle por maquina: {Trim(product, 20)}", 10, bold: true, 0.05m, 0.28m, 0.48m);
            writer.Text(MarginX + 166, y, "|", 10, bold: false, 0, 0, 0);
            writer.Text(MarginX + 180, y, $"Comision x metro: {Money(commissionUnit)}", 8, bold: false, 0, 0, 0);

            y -= 8;
            var widths = isTeamLeaderReport
                ? new[] { 58m, 56m, 132m, 145m, 74m, 76m, 74m, 74m, 73m }
                : new[] { 62m, 62m, 205m, 82m, 82m, 80m, 88m, 101m };
            var headers = isTeamLeaderReport
                ? new[] { "Fecha", "Cantidad", "Vendedor", "Cliente", "Precio x Metro", "Total vendido", "Comision", "Cobro adicional", "Total comision" }
                : new[] { "Fecha", "Cantidad", "Cliente", "Precio x Metro", "Total vendido", "Comision", "Cobro adicional", "Total comision" };
            DrawRow(writer, y, widths, headers, isHeader: true);
            y -= 13;

            foreach (var detail in group.OrderBy(x => x.Fecha).ThenBy(x => x.Cliente))
            {
                var comisionProducto = detail.Cantidad * detail.ComisionUnitario;
                var values = isTeamLeaderReport
                    ? new[]
                    {
                        detail.Fecha.ToString("dd/MM/yyyy"),
                        Quantity(detail.Cantidad),
                        Trim(detail.VendedorOrigen ?? "-", 20),
                        Trim(detail.Cliente, 24),
                        Number(detail.PrecioUnitario),
                        Number(detail.TotalDetalle),
                        Number(comisionProducto),
                        Number(detail.PrecioExtra),
                        Number(detail.ComisionTotal)
                    }
                    : new[]
                    {
                        detail.Fecha.ToString("dd/MM/yyyy"),
                        Quantity(detail.Cantidad),
                        Trim(detail.Cliente, 34),
                        Number(detail.PrecioUnitario),
                        Number(detail.TotalDetalle),
                        Number(comisionProducto),
                        Number(detail.PrecioExtra),
                        Number(detail.ComisionTotal)
                    };
                DrawRow(writer, y, widths, values);
                y -= 13;
            }

            var subtotalComisionProducto = group.Sum(x => x.Cantidad * x.ComisionUnitario);
            var subtotalValues = isTeamLeaderReport
                ? new[]
                {
                    "Subtotal",
                    Quantity(group.Sum(x => x.Cantidad)),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Number(group.Sum(x => x.TotalDetalle)),
                    Number(subtotalComisionProducto),
                    Number(group.Sum(x => x.PrecioExtra)),
                    Number(group.Sum(x => x.ComisionTotal))
                }
                : new[]
                {
                    "Subtotal",
                    Quantity(group.Sum(x => x.Cantidad)),
                    string.Empty,
                    string.Empty,
                    Number(group.Sum(x => x.TotalDetalle)),
                    Number(subtotalComisionProducto),
                    Number(group.Sum(x => x.PrecioExtra)),
                    Number(group.Sum(x => x.ComisionTotal))
                };
            DrawRow(writer, y, widths, subtotalValues, isSubtotal: true);

            return y - 13;
        }

        private static void DrawRow(PdfPageWriter writer, decimal y, IReadOnlyList<decimal> widths, IReadOnlyList<string> values, bool isHeader = false, bool isSubtotal = false)
        {
            var x = MarginX;
            for (var i = 0; i < widths.Count; i++)
            {
                var fill = isHeader
                    ? (TealR, TealG, TealB)
                    : isSubtotal
                        ? (0.60m, 0.84m, 0.90m)
                        : (0.98m, 0.99m, 1.00m);
                var text = isHeader ? (1m, 1m, 1m) : (0m, 0m, 0m);
                var center = isHeader || i == 0;
                var right = !isHeader && (widths.Count == 9
                    ? i is 1 or 4 or 5 or 6 or 7 or 8
                    : i is 1 or 3 or 4 or 5 or 6 or 7);

                writer.Cell(x, y, widths[i], 13, values[i], isHeader ? 7 : 8, bold: isHeader || isSubtotal, center: center, right: right, fill: fill, text: text);
                x += widths[i];
            }
        }

        private static string Quantity(decimal value)
        {
            return value.ToString("N2").TrimEnd('0').TrimEnd(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]);
        }

        private static string Number(decimal value)
        {
            return value.ToString("N0");
        }

        private static byte[] WriteDocument(IReadOnlyList<string> pages, PdfPngImage? logo)
        {
            var objects = new List<string>();
            var contentObjectIds = new List<int>();
            int? logoObjectId = null;

            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add(string.Empty);
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

            if (logo is not null)
            {
                var maskObjectId = objects.Count + 1;
                objects.Add(BuildImageObject(logo.AlphaBytes, logo.Width, logo.Height, "DeviceGray"));
                logoObjectId = objects.Count + 1;
                objects.Add(BuildImageObject(logo.RgbBytes, logo.Width, logo.Height, "DeviceRGB", maskObjectId));
            }

            foreach (var page in pages)
            {
                contentObjectIds.Add(objects.Count + 1);
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(page)} >>\nstream\n{page}\nendstream");
            }

            var pageObjectIds = new List<int>();
            foreach (var contentId in contentObjectIds)
            {
                pageObjectIds.Add(objects.Count + 1);
                var xObject = logoObjectId.HasValue ? $" /XObject << /Im1 {logoObjectId.Value} 0 R >>" : string.Empty;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xObject} >> /Contents {contentId} 0 R >>");
            }

            objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pageObjectIds.Count} >>";
            return WriteObjects(objects);
        }

        private static string BuildImageObject(byte[] data, int width, int height, string colorSpace, int? maskObjectId = null)
        {
            var encoded = Ascii85Encode(data);
            var mask = maskObjectId.HasValue ? $" /SMask {maskObjectId.Value} 0 R" : string.Empty;
            return $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /{colorSpace} /BitsPerComponent 8 /Filter [/ASCII85Decode /FlateDecode]{mask} /Length {encoded.Length} >>\nstream\n{encoded}\nendstream";
        }

        private static string Ascii85Encode(byte[] data)
        {
            var builder = new StringBuilder();
            var index = 0;
            var block = new byte[4];
            var chars = new char[5];

            while (index < data.Length)
            {
                Array.Clear(block);
                var count = Math.Min(4, data.Length - index);
                for (var i = 0; i < count; i++)
                {
                    block[i] = data[index + i];
                }

                var value = ((uint)block[0] << 24) | ((uint)block[1] << 16) | ((uint)block[2] << 8) | block[3];
                if (count == 4 && value == 0)
                {
                    builder.Append('z');
                }
                else
                {
                    for (var i = 4; i >= 0; i--)
                    {
                        chars[i] = (char)(value % 85 + 33);
                        value /= 85;
                    }

                    builder.Append(chars, 0, count + 1);
                }

                index += count;
            }

            builder.Append("~>");
            return builder.ToString();
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

        private sealed class PdfPageWriter
        {
            private readonly List<StringBuilder> _pages = new();
            private StringBuilder _builder;

            public PdfPageWriter()
            {
                _builder = new StringBuilder();
                _pages.Add(_builder);
            }

            public IEnumerable<string> Pages => _pages.Select(page => page.ToString());

            public void NewPage()
            {
                _builder = new StringBuilder();
                _pages.Add(_builder);
            }

            public void Text(decimal x, decimal y, string text, decimal size, bool bold, decimal r, decimal g, decimal b)
            {
                _builder.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1:0.##} Tf {2:0.##} {3:0.##} {4:0.###} rg {5:0.##} {6:0.##} Td ({7}) Tj ET\n",
                    bold ? "F2" : "F1",
                    size,
                    r,
                    g,
                    b,
                    x,
                    y,
                    Escape(text));
            }

            public void TextCenter(decimal x, decimal y, string text, decimal size, bool bold, decimal r, decimal g, decimal b)
            {
                var width = EstimateWidth(text, size);
                Text(x - width / 2, y, text, size, bold, r, g, b);
            }

            public void Cell(decimal x, decimal yTop, decimal width, decimal height, string value, decimal size, bool bold, bool center, bool right = false, (decimal r, decimal g, decimal b)? fill = null, (decimal r, decimal g, decimal b)? text = null)
            {
                var y = yTop - height;
                var fillColor = fill ?? (1m, 1m, 1m);
                _builder.AppendFormat(CultureInfo.InvariantCulture, "q {0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f Q\n",
                    fillColor.r,
                    fillColor.g,
                    fillColor.b,
                    x,
                    y,
                    width,
                    height);
                _builder.AppendFormat(CultureInfo.InvariantCulture, "q {0:0.###} {1:0.###} {2:0.###} RG {3:0.##} {4:0.##} {5:0.##} {6:0.##} re S Q\n",
                    BorderR,
                    BorderG,
                    BorderB,
                    x,
                    y,
                    width,
                    height);

                var textColor = text ?? (0m, 0m, 0m);
                var trimmed = Trim(value, Math.Max(4, (int)(width / (size * 0.48m))));
                var textX = x + 5;
                if (center)
                {
                    textX = x + (width - EstimateWidth(trimmed, size)) / 2;
                }
                else if (right)
                {
                    textX = x + width - EstimateWidth(trimmed, size) - 5;
                }

                Text(textX, y + 4, trimmed, size, bold, textColor.r, textColor.g, textColor.b);
            }

            public void Image(string name, decimal x, decimal y, decimal width, decimal height)
            {
                _builder.AppendFormat(CultureInfo.InvariantCulture, "q {0:0.##} 0 0 {1:0.##} {2:0.##} {3:0.##} cm /{4} Do Q\n", width, height, x, y, name);
            }

            private static decimal EstimateWidth(string text, decimal size)
            {
                return text.Length * size * 0.48m;
            }
        }

        private sealed record PdfPngImage(int Width, int Height, byte[] RgbBytes, byte[] AlphaBytes)
        {
            public static PdfPngImage? TryLoad(string path)
            {
                if (!System.IO.File.Exists(path))
                {
                    return null;
                }

                var bytes = System.IO.File.ReadAllBytes(path);
                if (bytes.Length < 33 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                {
                    return null;
                }

                var offset = 8;
                var width = 0;
                var height = 0;
                var colorType = 0;
                using var idat = new MemoryStream();

                while (offset + 12 <= bytes.Length)
                {
                    var length = ReadInt(bytes, offset);
                    offset += 4;
                    var chunkType = Encoding.ASCII.GetString(bytes, offset, 4);
                    offset += 4;

                    if (offset + length > bytes.Length)
                    {
                        return null;
                    }

                    if (chunkType == "IHDR")
                    {
                        width = ReadInt(bytes, offset);
                        height = ReadInt(bytes, offset + 4);
                        colorType = bytes[offset + 9];
                    }
                    else if (chunkType == "IDAT")
                    {
                        idat.Write(bytes, offset, length);
                    }
                    else if (chunkType == "IEND")
                    {
                        break;
                    }

                    offset += length + 4;
                }

                if (width <= 0 || height <= 0 || colorType != 6)
                {
                    return null;
                }

                var raw = Decompress(idat.ToArray());
                var rgba = UnfilterRgba(raw, width, height);
                var rgb = new byte[width * height * 3];
                var alpha = new byte[width * height];

                for (var source = 0; source < width * height; source++)
                {
                    rgb[source * 3] = rgba[source * 4];
                    rgb[source * 3 + 1] = rgba[source * 4 + 1];
                    rgb[source * 3 + 2] = rgba[source * 4 + 2];
                    alpha[source] = rgba[source * 4 + 3];
                }

                return new PdfPngImage(width, height, Compress(rgb), Compress(alpha));
            }

            private static byte[] Decompress(byte[] bytes)
            {
                using var input = new MemoryStream(bytes);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return output.ToArray();
            }

            private static byte[] Compress(byte[] bytes)
            {
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    zlib.Write(bytes, 0, bytes.Length);
                }

                return output.ToArray();
            }

            private static byte[] UnfilterRgba(byte[] raw, int width, int height)
            {
                const int bytesPerPixel = 4;
                var stride = width * bytesPerPixel;
                var result = new byte[stride * height];
                var rawOffset = 0;

                for (var row = 0; row < height; row++)
                {
                    var filter = raw[rawOffset++];
                    var rowOffset = row * stride;

                    for (var column = 0; column < stride; column++)
                    {
                        var value = raw[rawOffset++];
                        var left = column >= bytesPerPixel ? result[rowOffset + column - bytesPerPixel] : 0;
                        var up = row > 0 ? result[rowOffset + column - stride] : 0;
                        var upLeft = row > 0 && column >= bytesPerPixel ? result[rowOffset + column - stride - bytesPerPixel] : 0;

                        result[rowOffset + column] = filter switch
                        {
                            0 => value,
                            1 => (byte)(value + left),
                            2 => (byte)(value + up),
                            3 => (byte)(value + ((left + up) / 2)),
                            4 => (byte)(value + Paeth(left, up, upLeft)),
                            _ => value
                        };
                    }
                }

                return result;
            }

            private static int Paeth(int left, int up, int upLeft)
            {
                var estimate = left + up - upLeft;
                var leftDistance = Math.Abs(estimate - left);
                var upDistance = Math.Abs(estimate - up);
                var upLeftDistance = Math.Abs(estimate - upLeft);

                if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
                {
                    return left;
                }

                return upDistance <= upLeftDistance ? up : upLeft;
            }

            private static int ReadInt(byte[] bytes, int offset)
            {
                return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            }
        }
    }
}
