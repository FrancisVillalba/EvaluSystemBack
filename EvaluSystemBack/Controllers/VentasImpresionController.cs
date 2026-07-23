using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Security;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VentasImpresionController : ControllerBase
{
    private static readonly HashSet<string> EstadosVentaComisionables = new(StringComparer.OrdinalIgnoreCase) { "CO", "EE", "PE", "PI" };
    private const string ConfigMontoEnvioTransportadora = "MONTO_ENVIO_TRANSPORTADORA";
    private const int ConfigMontoEnvioTransportadoraNumero = 1;
    private const decimal MontoEnvioTransportadoraDefault = 10000;

    private readonly EvaluSystemDbContext _context;
    private readonly IVentaImpresionService _ventaImpresionService;
    private readonly IPermisoService _permisoService;
    private readonly IConfiguracionService _configuracionService;

    public VentasImpresionController(
        EvaluSystemDbContext context,
        IVentaImpresionService ventaImpresionService,
        IPermisoService permisoService,
        IConfiguracionService configuracionService)
    {
        _context = context;
        _ventaImpresionService = ventaImpresionService;
        _permisoService = permisoService;
        _configuracionService = configuracionService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<VentaImpresionCabDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? clienteId = null,
        [FromQuery] string? estadoVentaId = null,
        [FromQuery] int? vendedorId = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var filtered = await FilteredQueryAsync(search, dateFrom, dateTo, clienteId, estadoVentaId, vendedorId);
        if (filtered.Forbidden)
        {
            return Forbid();
        }

        var query = filtered.Query;

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

    [HttpGet("exportar-excel")]
    public async Task<ActionResult<ExcelFileDto>> ExportExcel(
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? clienteId = null,
        [FromQuery] string? estadoVentaId = null,
        [FromQuery] int? vendedorId = null)
    {
        var filtered = await FilteredQueryAsync(search, dateFrom, dateTo, clienteId, estadoVentaId, vendedorId);
        if (filtered.Forbidden)
        {
            return Forbid();
        }

        var items = await filtered.Query
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        var usuarios = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .Where(x => x.Estado != false)
            .ToListAsync();
        var vendedores = usuarios.ToDictionary(
            x => x.Id,
            x => x.Persona is null ? x.NombreUsuario ?? $"Usuario {x.Id}" : NombrePersona(x.Persona));
        var bytes = BuildOrdersXlsx(items, vendedores);

        return Ok(new ExcelFileDto(
            $"pedidos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Convert.ToBase64String(bytes)));
    }

    [HttpGet("opciones")]
    public async Task<ActionResult<VentaImpresionOptionsDto>> GetOptions()
    {
        var canViewAll = await CurrentUserCanViewAllOrdersAsync();
        var currentUserId = CurrentUserId();
        var canViewUserSales = canViewAll || (currentUserId.HasValue && await UserHasProfileAsync(currentUserId.Value, "Ventas"));

        var clientes = await _context.Clientes
            .Include(x => x.DatosEnvio)
            .AsNoTracking()
            .Where(x => x.Estado != false)
            .ToListAsync();
        var formasPago = await _context.FormasPago.AsNoTracking().Where(x => x.Estado != false).ToListAsync();
        var usuarios = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .Where(x => x.Estado != false)
            .ToListAsync();
        var vendedores = canViewAll
            ? usuarios
            : usuarios.Where(x => currentUserId.HasValue && x.Id == currentUserId.Value).ToList();
        var estadosPago = await _context.EstadosPago.AsNoTracking().Where(x => x.Estado != false).ToListAsync();
        var estadosVenta = await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => x.Estado == "A")
            .OrderBy(x => x.NumeroFlujo ?? int.MaxValue)
            .ThenBy(x => x.Nombre)
            .ToListAsync();
        var productos = await _context.Productos
            .Include(x => x.TipoMaquina)
            .AsNoTracking()
            .Where(x => x.Estado)
            .ToListAsync();
        var maquinas = await _context.TiposMaquina.AsNoTracking().Where(x => x.Estado).ToListAsync();
        var montoEnvioTransportadora = await MontoEnvioTransportadoraAsync();

        return Ok(new VentaImpresionOptionsDto(
            clientes.Select(x => x.ToDto()),
            formasPago.Select(x => x.ToDto()),
            vendedores.Select(x => x.ToDto()),
            estadosPago.Select(x => x.ToDto()),
            estadosVenta.Select(x => x.ToDto()),
            productos.Select(x => x.ToDto()),
            maquinas.Select(x => x.ToDto()),
            currentUserId,
            canViewAll,
            canViewUserSales,
            montoEnvioTransportadora));
    }

    private async Task<decimal> MontoEnvioTransportadoraAsync()
    {
        var valor = await _configuracionService.ObtenerValorAsync(
            ConfigMontoEnvioTransportadora,
            ConfigMontoEnvioTransportadoraNumero);

        if (decimal.TryParse(valor, out var monto) && monto >= 0)
        {
            return monto;
        }

        await _configuracionService.SaveAsync(new ConfiguracionRequest(
            ConfigMontoEnvioTransportadora,
            ConfigMontoEnvioTransportadoraNumero,
            MontoEnvioTransportadoraDefault.ToString()));

        return MontoEnvioTransportadoraDefault;
    }

    [HttpGet("mis-ventas")]
    public async Task<ActionResult<VentaUsuarioResumenDto>> GetMySales(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? clienteId = null,
        [FromQuery] string? estadoVentaId = null,
        [FromQuery] int? vendedorId = null)
    {
        var currentUserId = CurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Unauthorized();
        }

        var canViewAll = await CurrentUserCanViewAllOrdersAsync();
        if (!canViewAll && !await UserHasProfileAsync(currentUserId.Value, "Ventas"))
        {
            var today = DateTime.Today;
            return Ok(new VentaUsuarioResumenDto(
                dateFrom?.Date ?? new DateTime(today.Year, today.Month, 1),
                dateTo?.Date ?? today,
                false,
                new VentaUsuarioTotalesDto(0, 0, 0, 0),
                []));
        }

        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);
        var query = Query()
            .AsNoTracking()
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .AsQueryable();

        if (canViewAll)
        {
            if (vendedorId.HasValue)
            {
                query = query.Where(x => x.VendedorId == vendedorId.Value);
            }
        }
        else
        {
            query = query.Where(x => x.VendedorId == currentUserId.Value);
        }

        if (clienteId.HasValue)
        {
            query = query.Where(x => x.ClienteId == clienteId.Value);
        }

        if (!string.IsNullOrWhiteSpace(estadoVentaId))
        {
            query = query.Where(x => x.EstadoVentaId == estadoVentaId);
        }

        var ventas = await query
            .OrderByDescending(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(x => !IsDeleted(x.EstadoVentaId, x.EstadoVenta?.Nombre))
            .ToList();

        var perfilVentasId = await ProfileIdAsync("Ventas");
        var perfilVentaExternaId = await ProfileIdAsync("Venta Externa");
        var comisiones = await _context.ProductoComisiones
            .AsNoTracking()
            .Where(x => x.Estado)
            .Where(x => x.FechaHasta == null || x.FechaHasta >= from)
            .Where(x => x.FechaDesde == null || x.FechaDesde < toExclusive)
            .ToListAsync();
        var vendedorIds = ventas.Select(x => x.VendedorId).Distinct().ToHashSet();
        var perfilesPorUsuario = await _context.UsuarioPerfiles
            .Include(x => x.Perfil)
            .AsNoTracking()
            .Where(x => x.Estado && x.Perfil != null && x.Perfil.Estado && vendedorIds.Contains(x.UsuarioId))
            .GroupBy(x => x.UsuarioId)
            .ToDictionaryAsync(x => x.Key, x => x.Select(item => item.PerfilId).ToList());

        var items = ventas.Select(venta =>
        {
            var totalMetros = venta.Detalles.Sum(detalle => detalle.Cantidad);
            var perfilComisionId = canViewAll
                ? SellerCommissionProfileId(venta.VendedorId, perfilesPorUsuario, perfilVentasId, perfilVentaExternaId)
                : perfilVentasId;
            var totalComision = EstadosVentaComisionables.Contains(venta.EstadoVentaId)
                ? venta.Detalles.Where(EsDetalleComisionable).Sum(detalle =>
                    detalle.Cantidad * ResolveCommission(detalle.ProductoId, perfilComisionId, venta.FechaCreacion, comisiones) +
                    (detalle.PrecioExtra ?? 0))
                : 0;

            return new VentaUsuarioItemDto(
                venta.Id,
                venta.FechaCreacion,
                venta.Cliente?.Nombre ?? string.Empty,
                venta.EstadoVenta?.Nombre ?? venta.EstadoVentaId,
                venta.TotalVenta,
                totalMetros,
                totalComision);
        }).ToList();

        var totales = new VentaUsuarioTotalesDto(
            items.Count,
            items.Sum(x => x.TotalVenta),
            items.Sum(x => x.TotalMetros),
            items.Sum(x => x.TotalComision));

        return Ok(new VentaUsuarioResumenDto(from, to, true, totales, items));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VentaImpresionCabDto>> GetById(int id)
    {
        var item = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        if (!await CurrentUserCanViewAllOrdersAsync())
        {
            var currentUserId = CurrentUserId();
            if (!currentUserId.HasValue || item.VendedorId != currentUserId.Value)
            {
                return Forbid();
            }
        }

        return Ok(item.ToDto());
    }

    [HttpGet("dashboard")]
    [SkipPermission]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboard()
    {
        var currentUserId = CurrentUserId();
        if (!currentUserId.HasValue ||
            !await _permisoService.UsuarioTienePermisoAsync(currentUserId.Value, "Tablero", "ver"))
        {
            return Forbid();
        }

        var ventas = await Query().AsNoTracking().ToListAsync();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var ventasActivas = ventas.Where(x => !IsDeleted(x.EstadoVentaId, x.EstadoVenta?.Nombre)).ToList();
        var ventasDelDia = ventasActivas.Where(x => x.FechaCreacion.Date == today).ToList();
        var ventasDelMes = ventasActivas.Where(x => x.FechaCreacion >= monthStart && x.FechaCreacion < nextMonthStart).ToList();
        var vendedores = await _context.Personas
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => NombrePersona(x));

        var pedidosCargadosHoy = ventasDelDia.Count;
        var pedidosImpresos = ventasDelDia.Count(x => IsSent(x.EstadoVenta?.Nombre));
        var pedidosPendientesImpresion = ventasDelDia.Count(x => IsPendingPrint(x.EstadoVenta?.Nombre));
        var pedidosEntregadosHoy = ventasDelDia.Count(x => IsDelivered(x.EstadoVenta?.Nombre));
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

        var pedidosMensualesPorGrupo = ventasDelMes
            .SelectMany(x => x.Detalles.Select(d => new
            {
                Maquina = MachineGroupName(d.TipoMaquina?.Nombre),
                Monto = DetailAmount(d)
            }))
            .GroupBy(x => x.Maquina)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Monto), StringComparer.OrdinalIgnoreCase);
        var pedidosMensualesPorMaquina = new[] { "UV DTF", "DTF TEXTIL" }
            .Select(nombre => new DashboardMachineDto(nombre, pedidosMensualesPorGrupo.GetValueOrDefault(nombre, 0)))
            .ToList();
        var totalPedidosMensuales = pedidosMensualesPorMaquina.Sum(x => x.Cantidad);

        var metasMensuales = await GetMonthlyMachineGoalsAsync();
        var metaMensualTotal = metasMensuales.Values.Sum();
        var metasMensualesPorMaquina = new[] { "UV DTF", "DTF TEXTIL" }
            .Select(nombre =>
            {
                var cantidad = pedidosMensualesPorMaquina
                    .FirstOrDefault(x => string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase))
                    ?.Cantidad ?? 0;
                var meta = metasMensuales.GetValueOrDefault(nombre, 0);
                var faltante = Math.Max(meta - cantidad, 0);
                var porcentaje = meta > 0 ? Math.Min((cantidad / meta) * 100, 100) : cantidad > 0 ? 100 : 0;

                return new DashboardGoalDto(nombre, cantidad, meta, faltante, porcentaje, meta > 0 && cantidad >= meta);
            })
            .ToList();
        var faltanteTotal = Math.Max(metaMensualTotal - totalPedidosMensuales, 0);
        var porcentajeTotal = metaMensualTotal > 0
            ? Math.Min((totalPedidosMensuales / metaMensualTotal) * 100, 100)
            : totalPedidosMensuales > 0 ? 100 : 0;
        var metaMensualTotalDto = new DashboardGoalDto(
            "TOTAL",
            totalPedidosMensuales,
            metaMensualTotal,
            faltanteTotal,
            porcentajeTotal,
            metaMensualTotal > 0 && totalPedidosMensuales >= metaMensualTotal);

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
                x.Sum(item => item.TotalVenta)))
            .OrderByDescending(x => x.Monto)
            .Take(7)
            .ToList();

        return Ok(new DashboardSummaryDto(
            pedidosCargadosHoy,
            pedidosCargadosHoy,
            pedidosImpresos,
            pedidosPendientesImpresion,
            pedidosEntregadosHoy,
            totalPedidosMensuales,
            metaMensualTotalDto,
            pedidosPorMaquina,
            pedidosMensualesPorMaquina,
            metasMensualesPorMaquina,
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
        var vendedorValidation = await ValidateSellerForCurrentUserAsync(request.VendedorId);
        if (vendedorValidation is not null)
        {
            return vendedorValidation;
        }

        request = await NormalizeSellerAsync(request);
        var venta = await _ventaImpresionService.CrearVentaCompletaAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = venta.Id }, venta);
    }

    [HttpPut("completa/{id:int}")]
    public async Task<ActionResult<VentaImpresionCabDto>> UpdateCompleta(int id, VentaImpresionCompletaUpdateRequest request)
    {
        var vendedorValidation = await ValidateSellerForCurrentUserAsync(request.VendedorId);
        if (vendedorValidation is not null)
        {
            return vendedorValidation;
        }

        request = await NormalizeSellerAsync(request);
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
        var vendedorValidation = await ValidateSellerForCurrentUserAsync(request.VendedorId);
        if (vendedorValidation is not null)
        {
            return vendedorValidation;
        }

        request = await NormalizeSellerAsync(request);
        var venta = await _ventaImpresionService.ActualizarCabeceraAsync(id, request);
        return venta is null ? NotFound() : Ok(venta);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var eliminado = await _ventaImpresionService.EliminarVentaAsync(id);
            return eliminado ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}/marcar-eliminado")]
    public async Task<ActionResult<VentaImpresionCabDto>> MarkDeleted(int id, EliminarVentaImpresionRequest request)
    {
        try
        {
            var venta = await _ventaImpresionService.MarcarVentaEliminadaAsync(id, request);
            return venta is null ? NotFound() : Ok(venta);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private IQueryable<Models.VentaImpresionCab> Query()
    {
        return _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.FormaPago)
            .Include(x => x.EstadoPago)
            .Include(x => x.EstadoVenta)
            .Include(x => x.MetodoEnvio)
            .Include(x => x.UsuarioEntregaPedido).ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }

    private async Task<FilteredVentasQuery> FilteredQueryAsync(
        string? search,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? clienteId,
        string? estadoVentaId,
        int? vendedorId)
    {
        var query = Query().AsNoTracking();
        var canViewAll = await CurrentUserCanViewAllOrdersAsync();
        var currentUserId = CurrentUserId();

        if (!canViewAll)
        {
            if (!currentUserId.HasValue)
            {
                return new FilteredVentasQuery(query, true);
            }

            query = query.Where(x => x.VendedorId == currentUserId.Value);
        }
        else if (vendedorId.HasValue)
        {
            query = query.Where(x => x.VendedorId == vendedorId.Value);
        }

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
            var from = dateFrom.Value.Date;
            query = query.Where(x => x.FechaCreacion >= from);
        }

        if (dateTo.HasValue)
        {
            var to = dateTo.Value.Date.AddDays(1);
            query = query.Where(x => x.FechaCreacion < to);
        }

        if (clienteId.HasValue)
        {
            query = query.Where(x => x.ClienteId == clienteId.Value);
        }

        if (!string.IsNullOrWhiteSpace(estadoVentaId))
        {
            query = query.Where(x => x.EstadoVentaId == estadoVentaId);
        }

        return new FilteredVentasQuery(query, false);
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private async Task<bool> CurrentUserCanViewAllOrdersAsync()
    {
        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return false;
        }

        return await _permisoService.UsuarioTienePermisoAsync(userId.Value, "Administracion", "ver");
    }

    private async Task<bool> UserHasProfileAsync(int usuarioId, string profileName)
    {
        var hasProfile = await _context.UsuarioPerfiles
            .Include(x => x.Perfil)
            .AnyAsync(x => x.UsuarioId == usuarioId &&
                x.Estado &&
                x.Perfil != null &&
                x.Perfil.Estado &&
                x.Perfil.Nombre == profileName);

        return hasProfile;
    }

    private async Task<int> ProfileIdAsync(string profileName)
    {
        return await _context.Perfiles
            .AsNoTracking()
            .Where(x => x.Estado && x.Nombre == profileName)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();
    }

    private static bool EsDetalleComisionable(Models.VentaImpresionDet detalle)
    {
        return !string.Equals((detalle.EstadoItem ?? string.Empty).Trim(), "RE", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ResolveCommission(
        int productoId,
        int perfilId,
        DateTime fecha,
        IReadOnlyCollection<Models.ProductoComision> comisiones)
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

    private static int SellerCommissionProfileId(
        int vendedorId,
        IReadOnlyDictionary<int, List<int>> perfilesPorUsuario,
        int perfilVentasId,
        int perfilVentaExternaId)
    {
        if (!perfilesPorUsuario.TryGetValue(vendedorId, out var perfilIds) || perfilIds.Count == 0)
        {
            return 0;
        }

        if (perfilVentaExternaId > 0 && perfilIds.Contains(perfilVentaExternaId))
        {
            return perfilVentaExternaId;
        }

        if (perfilVentasId > 0 && perfilIds.Contains(perfilVentasId))
        {
            return perfilVentasId;
        }

        return perfilIds[0];
    }

    private async Task<ActionResult?> ValidateSellerForCurrentUserAsync(int vendedorId)
    {
        if (await CurrentUserCanViewAllOrdersAsync())
        {
            return null;
        }

        var userId = CurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        return vendedorId == userId.Value
            ? null
            : Forbid();
    }

    private async Task<VentaImpresionCompletaRequest> NormalizeSellerAsync(VentaImpresionCompletaRequest request)
    {
        return await CurrentUserCanViewAllOrdersAsync() || !CurrentUserId().HasValue
            ? request
            : request with { VendedorId = CurrentUserId()!.Value };
    }

    private async Task<VentaImpresionCompletaUpdateRequest> NormalizeSellerAsync(VentaImpresionCompletaUpdateRequest request)
    {
        return await CurrentUserCanViewAllOrdersAsync() || !CurrentUserId().HasValue
            ? request
            : request with { VendedorId = CurrentUserId()!.Value };
    }

    private async Task<VentaImpresionCabRequest> NormalizeSellerAsync(VentaImpresionCabRequest request)
    {
        return await CurrentUserCanViewAllOrdersAsync() || !CurrentUserId().HasValue
            ? request
            : request with { VendedorId = CurrentUserId()!.Value };
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

    private static string NombreUsuario(Models.Usuario usuario)
    {
        return usuario.Persona is null
            ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}"
            : NombrePersona(usuario.Persona);
    }

    private static string MetodoEntregaLabel(string? metodoEntrega)
    {
        return (metodoEntrega ?? "DELIVERY").ToUpperInvariant() switch
        {
            "DELIVERY" => "Delivery",
            "RETIRO_LOCAL" => "Retiro en local",
            "MOTOBOLT" => "Motobolt",
            "TRANSPORTADORA" => "Transportadora",
            "OTRO" => "Otro",
            _ => metodoEntrega ?? "Delivery"
        };
    }

    private static bool IsPrinted(string? estado)
    {
        return StatusContains(estado, "impres")
            || IsSent(estado);
    }

    private static bool IsDelivered(string? estado)
    {
        return StatusContains(estado, "enviado")
            || StatusContains(estado, "entregado");
    }

    private static bool IsDeleted(string? estadoId, string? estado)
    {
        return StatusContains(estadoId, "elimin")
            || StatusContains(estadoId, "eli")
            || StatusContains(estado, "elimin");
    }

    private static bool IsSent(string? estado)
    {
        return StatusContains(estado, "envio")
            || StatusContains(estado, "enviado")
            || StatusContains(estado, "entregado");
    }

    private static bool IsPendingPrint(string? estado)
    {
        return StatusContains(estado, "carga")
            || StatusContains(estado, "impresion");
    }

    private static bool StatusContains(string? estado, string text)
    {
        if (string.IsNullOrWhiteSpace(estado))
        {
            return false;
        }

        var normalized = estado.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildOrdersXlsx(
        IEnumerable<Models.VentaImpresionCab> pedidos,
        IReadOnlyDictionary<int, string> vendedores)
    {
        var rows = new List<string[]>
        {
            new[] { "Pedido", "Fecha carga", "Cliente", "Vendedor", "Tipo", "Metros", "Estado", "Metodo entrega", "Delivery", "Fecha tomado", "Entrega" }
        };

        rows.AddRange(pedidos.Select(pedido => new[]
        {
            pedido.Id.ToString(),
            pedido.FechaCreacion.ToString("yyyy-MM-dd"),
            pedido.Cliente?.Nombre ?? string.Empty,
            vendedores.GetValueOrDefault(pedido.VendedorId, $"Usuario {pedido.VendedorId}"),
            OrderTypeFromDetails(pedido.Detalles),
            MetersFromDetails(pedido.Detalles),
            pedido.EstadoVenta?.Nombre ?? pedido.EstadoVentaId,
            MetodoEntregaLabel(pedido.MetodoEntrega),
            pedido.UsuarioEntregaPedido is null ? string.Empty : NombreUsuario(pedido.UsuarioEntregaPedido),
            pedido.FechaTomaDelivery?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            pedido.FechaEntrega?.ToString("yyyy-MM-dd") ?? string.Empty
        }));

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
                    <sheets>
                        <sheet name="Pedidos" sheetId="1" r:id="rId1"/>
                    </sheets>
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
                    <fonts count="2">
                        <font><sz val="11"/><name val="Calibri"/></font>
                        <font><b/><sz val="11"/><name val="Calibri"/></font>
                    </fonts>
                    <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
                    <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                    <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                    <cellXfs count="2">
                        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                        <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
                    </cellXfs>
                    <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                </styleSheet>
                """);
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows));
        }

        return stream.ToArray();
    }

    private static string BuildWorksheetXml(IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("<sheetData>");

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
                    .Append(XmlValue(rows[rowIndex][columnIndex]))
                    .AppendLine("</t></is></c>");
            }

            builder.AppendLine("</row>");
        }

        builder.AppendLine("</sheetData>");
        builder.AppendLine("</worksheet>");
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

    private static string XmlValue(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string OrderTypeFromDetails(IEnumerable<Models.VentaImpresionDet> details)
    {
        var product = details.FirstOrDefault()?.Producto?.Nombre ?? "DTF Textil";
        return product.Contains("UV", StringComparison.OrdinalIgnoreCase) ? "UV DTF" : "DTF Textil";
    }

    private static string MetersFromDetails(IEnumerable<Models.VentaImpresionDet> details)
    {
        var total = details.Sum(detail => detail.Cantidad);
        return total == 0 ? "0 m" : $"{total:N2} m";
    }

    private static decimal DetailAmount(Models.VentaImpresionDet detail)
    {
        return detail.PrecioTotal ?? (detail.Cantidad * detail.PrecioUnitario) + (detail.PrecioExtra ?? 0);
    }

    private async Task<Dictionary<string, decimal>> GetMonthlyMachineGoalsAsync()
    {
        var configurations = await _context.Configuraciones
            .AsNoTracking()
            .Where(x => x.Nombre.ToUpper().Contains("META") && x.Nombre.ToUpper().Contains("MENSUAL"))
            .ToListAsync();
        var machines = await _context.TiposMaquina
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Nombre);
        var goals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["UV DTF"] = 0,
            ["DTF TEXTIL"] = 0
        };

        foreach (var configuration in configurations)
        {
            if (!decimal.TryParse(configuration.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var goal) || goal <= 0)
            {
                continue;
            }

            var machineName = machines.GetValueOrDefault(configuration.NroConfiguracion)
                ?? configuration.Nombre
                    .Replace("META_MENSUAL", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("Meta mensual", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("_", " ")
                    .Trim();
            var groupName = MachineGroupName(machineName);

            if (goals.ContainsKey(groupName))
            {
                goals[groupName] += goal;
            }
        }

        return goals;
    }

    private static string MachineGroupName(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return "Sin maquina";
        }

        var normalized = RemoveDiacritics(machineName);
        if (normalized.Contains("uv", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("dtf", StringComparison.OrdinalIgnoreCase))
        {
            return "UV DTF";
        }

        if (normalized.Contains("dtf", StringComparison.OrdinalIgnoreCase))
        {
            return "DTF TEXTIL";
        }

        return machineName;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed record FilteredVentasQuery(IQueryable<Models.VentaImpresionCab> Query, bool Forbidden);
}
