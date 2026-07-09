using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
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
    private const int FlujoImpresion = 2;
    private const int FlujoPendienteEnvio = 3;
    private const int FlujoEntregado = 4;
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
        [FromQuery] int? vendedorId = null)
    {
        return Ok(await BuildComisionesAsync(dateFrom, dateTo, vendedorId));
    }

    [HttpGet("comisiones-vendedores/excel")]
    public async Task<ActionResult<ExcelFileDto>> ExportComisionesExcel(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null)
    {
        var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId);
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
        [FromQuery] int? vendedorId = null)
    {
        var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId);
        var bytes = CommissionPdfBuilder.Build(report, _environment.WebRootPath);
        var sellerFilePart = await ReportFileSellerNameAsync(vendedorId, report);

        return Ok(new ExcelFileDto(
            $"{sellerFilePart}-{DateTime.Now:yyyyMMddHHmm}.pdf",
            "application/pdf",
            Convert.ToBase64String(bytes)));
    }

    [HttpGet("comisiones-vendedores/txt")]
    public async Task<ActionResult<ExcelFileDto>> ExportComisionesBancoTxt(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null)
    {
        try
        {
            var report = await BuildComisionesAsync(dateFrom, dateTo, vendedorId);
            var lote = await GetOrCreateComisionesLoteAsync(report, vendedorId);

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
    public async Task<ActionResult<IEnumerable<LotePagoDto>>> GetLotesPago([FromQuery] string? tipoPago = null)
    {
        var query = _context.LotesPago
            .Include(x => x.UsuarioGenero).ThenInclude(x => x!.Persona)
            .Include(x => x.Vendedor).ThenInclude(x => x!.Persona)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tipoPago))
        {
            query = query.Where(x => x.TipoPago == tipoPago);
        }

        var lotes = await query
            .OrderByDescending(x => x.FechaGeneracion)
            .Take(100)
            .ToListAsync();

        return Ok(lotes.Select(ToLotePagoDto));
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
            .Where(x => (x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion) >= from)
            .Where(x => (x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion) < toExclusive)
            .Where(x => x.EstadoVenta != null && x.EstadoVenta.NumeroFlujo == 4)
            .Where(x => string.IsNullOrWhiteSpace(method) || x.MetodoEntrega == method)
            .OrderBy(x => x.MetodoEntrega)
            .ThenBy(x => x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .ToListAsync();

        ventas = ventas
            .Where(x => x.EstadoVenta?.Nombre?.Contains("elimin", StringComparison.OrdinalIgnoreCase) != true)
            .Where(x => string.IsNullOrWhiteSpace(clientSearch) || (x.Cliente?.Nombre ?? string.Empty).Contains(clientSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var detalles = ventas.Select(x => new ReporteEnvioDetalleDto(
            x.Id,
            x.FechaTomaDelivery ?? x.FechaEntrega ?? x.FechaCreacion,
            x.Cliente?.Nombre ?? string.Empty,
            x.MetodoEntrega,
            MetodoEntregaLabel(x.MetodoEntrega),
            x.EstadoVenta?.Nombre ?? x.EstadoVentaId,
            x.UsuarioEntregaPedido is null ? null : NombreUsuario(x.UsuarioEntregaPedido),
            x.Cliente?.DatosEnvio?.Ciudad?.Nombre ?? x.Cliente?.Ciudad?.Nombre,
            x.TotalVenta)).ToList();

        var resumen = ventas
            .GroupBy(x => new
            {
                x.UsuarioEntregaPedidoId,
                UsuarioEntrega = x.UsuarioEntregaPedido is null ? "Sin usuario entrega" : NombreUsuario(x.UsuarioEntregaPedido)
            })
            .Select(group =>
            {
                var cantidadTransportadora = group.Count(x => string.Equals(x.MetodoEntrega, "TRANSPORTADORA", StringComparison.OrdinalIgnoreCase));
                return new ReporteEnvioResumenDto(
                    group.Key.UsuarioEntregaPedidoId,
                    group.Key.UsuarioEntrega,
                    group.Count(),
                    cantidadTransportadora,
                    group.Sum(x => x.TotalVenta));
            })
            .OrderBy(x => x.UsuarioEntrega)
            .ToList();

        return Ok(new ReporteEnviosDto(from, to, resumen, detalles));
    }

    private async Task<ReporteComisionesDto> BuildComisionesAsync(DateTime? dateFrom, DateTime? dateTo, int? vendedorId)
    {
        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.EstadoVenta)
            .AsNoTracking()
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .Where(x => vendedorId == null || x.VendedorId == vendedorId.Value)
            .Where(x =>
                x.EstadoVenta != null &&
                (x.EstadoVenta.NumeroFlujo == FlujoImpresion ||
                 x.EstadoVenta.NumeroFlujo == FlujoPendienteEnvio ||
                 x.EstadoVenta.NumeroFlujo == FlujoEntregado))
            .OrderBy(x => x.VendedorId)
            .ThenBy(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(x => x.EstadoVenta?.Nombre?.Contains("elimin", StringComparison.OrdinalIgnoreCase) != true)
            .ToList();

        var vendedores = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Persona is null ? x.NombreUsuario ?? $"Usuario {x.Id}" : NombrePersona(x.Persona));
        var perfilesPorUsuario = await _context.UsuarioPerfiles
            .Include(x => x.Perfil)
            .AsNoTracking()
            .Where(x => x.Estado && x.Perfil != null && x.Perfil.Estado)
            .GroupBy(x => x.UsuarioId)
            .ToDictionaryAsync(x => x.Key, x => x.Select(item => item.PerfilId).ToList());
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

        var detallesComision = new List<(int UsuarioId, ReporteComisionDetalleDto Detalle)>();
        foreach (var venta in ventas)
        {
            foreach (var detalle in venta.Detalles)
            {
                detallesComision.Add((venta.VendedorId, BuildComisionDetalle(venta, detalle, venta.VendedorId, perfilesPorUsuario, comisiones, incluirExtra: true)));

                if (teamLeadersPorVendedor.TryGetValue(venta.VendedorId, out var teamLeaderId))
                {
                    detallesComision.Add((teamLeaderId, BuildComisionDetalle(venta, detalle, teamLeaderId, perfilesPorUsuario, comisiones, incluirExtra: false)));
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
            .OrderBy(x => x.Vendedor)
            .ToList();

        return new ReporteComisionesDto(from, to, grouped);
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

    private async Task<LotePago> GetOrCreateComisionesLoteAsync(ReporteComisionesDto report, int? vendedorId)
    {
        var existing = await _context.LotesPago
            .AsNoTracking()
            .Where(x => x.TipoPago == TipoPagoComisiones)
            .Where(x => x.FechaDesde == report.FechaDesde.Date && x.FechaHasta == report.FechaHasta.Date)
            .Where(x => x.FechaPago == report.FechaHasta.Date)
            .Where(x => x.VendedorId == vendedorId)
            .Where(x => x.Estado != EstadoLoteAnulado)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return existing;
        }

        var file = await BuildComisionesBancoTxtAsync(report, report.FechaHasta.Date);
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
            MontoTotal = file.Rows.Sum(x => x.Monto),
            CantidadPersonas = file.Rows.Count,
            NombreArchivo = $"banco-continental-comisiones-{sellerFilePart}-{DateTime.Now:yyyyMMddHHmm}.txt",
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

    private async Task<BankTxtFile> BuildComisionesBancoTxtAsync(ReporteComisionesDto report, DateTime fechaPago)
    {
        var cuentaDebitoEmpresa = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 1, "012312345699");
        var concepto = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 2, "SALARIO");
        var esAguinaldo = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 3, "NO");
        var tipoCuenta = await ConfigValueAsync("BANCO_CONTINENTAL_COMISIONES", 4, "CC");

        if (string.IsNullOrWhiteSpace(cuentaDebitoEmpresa))
        {
            throw new InvalidOperationException("Falta configurar la cuenta de debito de la empresa para Banco Continental.");
        }

        var sellerIds = report.Vendedores
            .Where(x => x.TotalComision > 0)
            .Select(x => x.VendedorId)
            .Distinct()
            .ToList();

        var sellers = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .Where(x => sellerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var rows = new List<BankTxtRow>();
        var missing = new List<string>();

        foreach (var seller in report.Vendedores.Where(x => x.TotalComision > 0).OrderBy(x => x.Vendedor))
        {
            sellers.TryGetValue(seller.VendedorId, out var usuario);
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
                seller.VendedorId,
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

        public static byte[] Build(ReporteComisionesDto report, string? webRootPath)
        {
            var pages = new List<string>();
            var logo = PdfPngImage.TryLoad(GetLogoPath(webRootPath));

            foreach (var seller in report.Vendedores.DefaultIfEmpty(new ReporteComisionVendedorDto(0, "Sin ventas", 0, 0, 0, Array.Empty<ReporteComisionDetalleDto>())))
            {
                var writer = new PdfPageWriter();
                DrawSellerReport(writer, report, seller, logo);
                pages.AddRange(writer.Pages);
            }

            return WriteDocument(pages, logo);
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

        private static void DrawSellerReport(PdfPageWriter writer, ReporteComisionesDto report, ReporteComisionVendedorDto seller, PdfPngImage? logo)
        {
            var y = PageHeight - 32;
            if (logo is not null)
            {
                var logoWidth = 96m;
                var logoHeight = logoWidth * logo.Height / logo.Width;
                writer.Image("Im1", MarginX, y - logoHeight, logoWidth, logoHeight);
            }

            writer.TextCenter(PageWidth / 2, y - 28, "REPORTE DE COMISIONES POR VENDEDOR", 17, bold: true, TealR, TealG, TealB);

            y -= 58;
            DrawSummaryTable(writer, y, report, seller);
            y -= 66;

            var groups = seller.Detalles
                .GroupBy(detail => new { detail.Producto, detail.ComisionUnitario, detail.PrecioUnitario })
                .OrderBy(group => group.Key.Producto)
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

                y = DrawProductGroup(writer, y, group.Key.Producto, group.Key.ComisionUnitario, group);
                y -= 20;
            }
        }

        private static void DrawSummaryTable(PdfPageWriter writer, decimal yTop, ReporteComisionesDto report, ReporteComisionVendedorDto seller)
        {
            var x = MarginX;
            var totalWidth = PageWidth - MarginX * 2;
            var colWidth = totalWidth / 4;
            var headerHeight = 24;
            var valueHeight = 28;
            var headers = new[] { "Vendedor", "Rango de fecha", "Total vendido", "Total comision" };
            var values = new[]
            {
                Trim(seller.Vendedor, 30),
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
            IGrouping<dynamic, ReporteComisionDetalleDto> group)
        {
            writer.Text(MarginX, y, $"Detalle por maquina: {Trim(product, 20)}", 10, bold: true, 0.05m, 0.28m, 0.48m);
            writer.Text(MarginX + 166, y, "|", 10, bold: false, 0, 0, 0);
            writer.Text(MarginX + 180, y, $"Comision x metro: {Money(commissionUnit)}", 8, bold: false, 0, 0, 0);

            y -= 8;
            var widths = new[] { 62m, 62m, 205m, 82m, 82m, 80m, 88m, 101m };
            var headers = new[] { "Fecha", "Cantidad", "Cliente", "Precio x Metro", "Total vendido", "Comision", "Cobro adicional", "Total comision" };
            DrawRow(writer, y, widths, headers, isHeader: true);
            y -= 13;

            foreach (var detail in group.OrderBy(x => x.Fecha).ThenBy(x => x.Cliente))
            {
                var comisionProducto = detail.Cantidad * detail.ComisionUnitario;
                DrawRow(writer, y, widths, new[]
                {
                    detail.Fecha.ToString("dd/MM/yyyy"),
                    Quantity(detail.Cantidad),
                    Trim(detail.Cliente, 34),
                    Number(detail.PrecioUnitario),
                    Number(detail.TotalDetalle),
                    Number(comisionProducto),
                    Number(detail.PrecioExtra),
                    Number(detail.ComisionTotal)
                });
                y -= 13;
            }

            var subtotalComisionProducto = group.Sum(x => x.Cantidad * x.ComisionUnitario);
            DrawRow(writer, y, widths, new[]
            {
                "Subtotal",
                Quantity(group.Sum(x => x.Cantidad)),
                string.Empty,
                string.Empty,
                Number(group.Sum(x => x.TotalDetalle)),
                Number(subtotalComisionProducto),
                Number(group.Sum(x => x.PrecioExtra)),
                Number(group.Sum(x => x.ComisionTotal))
            }, isSubtotal: true);

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
                var right = !isHeader && i is 1 or 3 or 4 or 5 or 6 or 7;

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
