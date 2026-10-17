using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using EvaluSystemBack.Data;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using EvaluSystemBack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PagosController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    public PagosController(EvaluSystemDbContext context) => _context = context;

    public record PagoPedidoDto(int PedidoId, DateTime Fecha, string Vendedor, decimal TotalVenta,
        decimal MontoPagado, decimal SaldoPendiente, string EstadoPago, IEnumerable<PagoComprobanteDto> ComprobantesTransferencia);
    public record PagoComprobanteDto(DateTime Fecha, decimal Monto, string Ruta, string Nombre);
    public record RevisionPagoRequest([Required] string Estado, [MaxLength(2000)] string? Comentario);
    public record RevisionPagoRow(string Cliente, string? Telefono, PagoPedidoDto Pedido,
        string Estado, string? Comentario, int? UsuarioId, DateTime? FechaRevision);

    [HttpGet("revision-pagos/problemas/excel")]
    public async Task<IActionResult> ExportProblemasPago(DateTime? dateFrom = null, DateTime? dateTo = null, string? cliente = null)
    {
        var result = await GetRevisionPagos(dateFrom, dateTo, cliente, "PROBLEMA");
        if (result is not OkObjectResult { Value: IEnumerable<RevisionPagoRow> problemas }) return result;
        var rows = new List<string[]> { new[] { "Cliente", "Teléfono", "Pedido", "Fecha del pedido", "Vendedor",
            "Total (Gs.)", "Pagado (Gs.)", "Problema", "Fecha de revisión", "Usuario" } };
        rows.AddRange(problemas.Select(r => new[] { r.Cliente, r.Telefono ?? "", r.Pedido.PedidoId.ToString(),
            r.Pedido.Fecha.ToString("yyyy-MM-dd HH:mm"), r.Pedido.Vendedor,
            r.Pedido.TotalVenta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.Pedido.MontoPagado.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Comentario ?? "",
            r.FechaRevision?.ToString("yyyy-MM-dd HH:mm") ?? "", r.UsuarioId?.ToString() ?? "" }));
        return Ok(new { FileName = $"problemas-pagos-{DateTime.Now:yyyyMMddHHmm}.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Bytes = Convert.ToBase64String(BuildExcelWorkbook(rows, "Problemas de pagos")) });
    }

    [HttpGet("revision-pagos")]
    public async Task<IActionResult> GetRevisionPagos(DateTime? dateFrom = null, DateTime? dateTo = null,
        string? cliente = null, string estado = "PENDIENTE")
    {
        if (dateFrom > dateTo) return BadRequest(new { message = "El rango de fechas no es válido." });
        if (estado is not ("PENDIENTE" or "PROBLEMA" or "OK")) return BadRequest(new { message = "Estado inválido." });
        var from = (dateFrom ?? new DateTime(1900, 1, 1)).Date;
        var toExclusive = (dateTo ?? DateTime.Today).Date.AddDays(1);
        var ventas = await _context.VentasImpresionCab.AsNoTracking()
            .Include(x => x.Cliente).Include(x => x.EstadoVenta).Include(x => x.EstadoPago).Include(x => x.Pagos)
            .Where(x => x.EstadoPagadoId == "P3" && !x.Reposicion)
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .ToListAsync();
        var search = cliente?.Trim();
        ventas = ventas.Where(x => !IsExcludedFromPaymentReport(x.EstadoVentaId, x.EstadoVenta?.Nombre))
            .Where(x => string.IsNullOrWhiteSpace(search) || (x.Cliente?.Nombre ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        var formasTransferencia = (await _context.FormasPago.AsNoTracking()
            .Where(x => x.Nombre != null && x.Nombre.Trim().ToLower() == "transferencia")
            .Select(x => x.Id).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vendedorIds = ventas.Select(x => x.VendedorId).Distinct().ToArray();
        var vendedores = await _context.Usuarios.AsNoTracking().Include(x => x.Persona)
            .Where(x => vendedorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, NombreUsuario);
        var revisiones = await _context.RevisionesPago.AsNoTracking()
            .Where(x => !_context.RevisionesPago.Any(y => y.PedidoId == x.PedidoId && y.Id > x.Id))
            .ToDictionaryAsync(x => x.PedidoId);
        var rows = ventas.Select(v =>
        {
            revisiones.TryGetValue(v.Id, out var revision);
            var pedido = new PagoPedidoDto(v.Id, v.FechaCreacion,
                vendedores.GetValueOrDefault(v.VendedorId, $"Usuario {v.VendedorId}"), v.TotalVenta,
                v.MontoPagado ?? 0, Math.Max(v.TotalVenta - (v.MontoPagado ?? 0), 0),
                v.EstadoPago?.Nombre ?? "Pagado", BuildTransferProofs(v, formasTransferencia));
            return new RevisionPagoRow(v.Cliente?.Nombre ?? $"Cliente {v.ClienteId}", v.Cliente?.NroTelefono,
                pedido, revision?.Estado ?? "PENDIENTE", revision?.Comentario, revision?.UsuarioId, revision?.Fecha);
        }).Where(x => x.Estado == estado).OrderByDescending(x => x.Pedido.Fecha).ToList();
        return Ok(rows);
    }

    [HttpPut("revision-pagos/{pedidoId:int}")]
    public async Task<IActionResult> RevisarPago(int pedidoId, RevisionPagoRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId))
            return Unauthorized();
        if (request.Estado is not ("OK" or "PROBLEMA")) return BadRequest(new { message = "Estado inválido." });
        if (request.Estado == "PROBLEMA" && string.IsNullOrWhiteSpace(request.Comentario))
            return BadRequest(new { message = "Escriba un comentario sobre el problema." });
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var venta = await _context.VentasImpresionCab.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pedidoId, cancellationToken);
        if (venta is null) return NotFound();
        if (venta.EstadoPagadoId != "P3" || venta.Reposicion)
            return Conflict(new { message = "El pedido ya no está pagado. Actualice la lista." });
        _context.RevisionesPago.Add(new RevisionPago { PedidoId = pedidoId, Estado = request.Estado,
            Comentario = request.Comentario?.Trim(), UsuarioId = usuarioId, Fecha = DateTime.Now });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
    private static byte[] BuildExcelWorkbook(IReadOnlyList<string[]> rows, string sheetName)
    {
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
                """.Replace("Comisiones", WebUtility.HtmlEncode(sheetName)));
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

    private static bool IsExcludedFromPaymentReport(string? estadoId, string? estado)
    {
        var normalizedId = (estadoId ?? string.Empty).Trim();
        var normalizedState = (estado ?? string.Empty).Trim();
        return normalizedId.Equals("XX", StringComparison.OrdinalIgnoreCase)
            || normalizedId.Equals("RE", StringComparison.OrdinalIgnoreCase)
            || normalizedId.Contains("elimin", StringComparison.OrdinalIgnoreCase)
            || normalizedState.Contains("elimin", StringComparison.OrdinalIgnoreCase);
    }
    private static IReadOnlyList<PagoComprobanteDto> BuildTransferProofs(
        VentaImpresionCab venta,
        IReadOnlySet<string> formasTransferencia)
    {
        var comprobantes = venta.Pagos
            .Where(p => formasTransferencia.Contains(p.FormaPagoId) && !string.IsNullOrWhiteSpace(p.RutaComprobante))
            .OrderByDescending(p => p.FechaHora)
            .Select(p => new PagoComprobanteDto(
                p.FechaHora,
                p.Monto,
                p.RutaComprobante!,
                string.IsNullOrWhiteSpace(p.NombreComprobante) ? "Comprobante de transferencia" : p.NombreComprobante))
            .ToList();

        if (!formasTransferencia.Contains(venta.FormaPagoId) || string.IsNullOrWhiteSpace(venta.ComprobantePago))
        {
            return comprobantes;
        }

        var rutas = ParseStoredValues(venta.ComprobantePago);
        var nombres = ParseStoredValues(venta.ComprobantePagoNombre);
        for (var index = 0; index < rutas.Count; index++)
        {
            var ruta = rutas[index];
            if (string.IsNullOrWhiteSpace(ruta) || comprobantes.Any(x => string.Equals(x.Ruta, ruta, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var nombre = index < nombres.Count && !string.IsNullOrWhiteSpace(nombres[index])
                ? nombres[index]
                : "Comprobante de transferencia";
            comprobantes.Add(new PagoComprobanteDto(
                venta.FechaCreacion,
                venta.MontoPagado ?? 0,
                ruta,
                nombre));
        }

        return comprobantes.OrderByDescending(x => x.Fecha).ToList();
    }

    private static IReadOnlyList<string> ParseStoredValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new[] { value };
        }
    }
    private static string NombreUsuario(Usuario usuario)
    {
        return usuario.Persona is null ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}" : NombrePersona(usuario.Persona);
    }

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
}