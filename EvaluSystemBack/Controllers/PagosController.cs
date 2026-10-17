using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using EvaluSystemBack.Data;
using System.Globalization;

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
        string Estado, string? Comentario, int? UsuarioId, DateTime? FechaRevision, string? UsuarioNombre);

    [HttpGet("opciones")]
    public async Task<IActionResult> GetOpciones(CancellationToken cancellationToken)
    {
        var formasPago = await _context.FormasPago.AsNoTracking().Where(x => x.Estado != false)
            .OrderBy(x => x.Nombre).Select(x => new { x.Id, x.Nombre }).ToListAsync(cancellationToken);
        var usuarios = await _context.Usuarios.AsNoTracking().Include(x => x.Persona)
            .Where(x => x.Estado != false).ToListAsync(cancellationToken);
        var vendedores = usuarios.Select(x => new { x.Id, Nombre = NombreUsuario(x) }).OrderBy(x => x.Nombre);
        return Ok(new { formasPago, vendedores });
    }

    [HttpGet("revision-pagos/incorrectos/pdf")]
    public async Task<IActionResult> ExportProblemasPago(DateTime? dateFrom = null, DateTime? dateTo = null, string? cliente = null,
        string? formaPagoId = null, int? vendedorId = null)
    {
        var result = await GetRevisionPagos(dateFrom, dateTo, cliente, "PROBLEMA", formaPagoId, vendedorId);
        if (result is not OkObjectResult { Value: IEnumerable<RevisionPagoRow> problemas }) return result;
        return Ok(new { FileName = $"pagos-incorrectos-{DateTime.Now:yyyyMMddHHmm}.pdf",
            ContentType = "application/pdf",
            Bytes = Convert.ToBase64String(BuildIncorrectosPdf(problemas.ToList())) });
    }
    [HttpGet("revision-pagos")]
    public async Task<IActionResult> GetRevisionPagos(DateTime? dateFrom = null, DateTime? dateTo = null,
        string? cliente = null, string estado = "PENDIENTE", string? formaPagoId = null, int? vendedorId = null)
    {
        if (dateFrom > dateTo) return BadRequest(new { message = "El rango de fechas no es válido." });
        if (estado is not ("PENDIENTE" or "PROBLEMA" or "OK")) return BadRequest(new { message = "Estado inválido." });
        var from = (dateFrom ?? new DateTime(1900, 1, 1)).Date;
        var toExclusive = (dateTo ?? DateTime.Today).Date.AddDays(1);
        var paymentMethod = formaPagoId?.Trim();
        var ventas = await _context.VentasImpresionCab.AsNoTracking()
            .Include(x => x.Cliente).Include(x => x.EstadoVenta).Include(x => x.EstadoPago).Include(x => x.Pagos)
            .Where(x => x.EstadoPagadoId == "P3" && !x.Reposicion)
            .Where(x => string.IsNullOrWhiteSpace(paymentMethod) || x.FormaPagoId == paymentMethod)
            .Where(x => !vendedorId.HasValue || x.VendedorId == vendedorId.Value)
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
        var revisorIds = revisiones.Values.Select(x => x.UsuarioId).Distinct().ToArray();
        var revisores = await _context.Usuarios.AsNoTracking().Include(x => x.Persona)
            .Where(x => revisorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, NombreRevisor);
        var rows = ventas.Select(v =>
        {
            revisiones.TryGetValue(v.Id, out var revision);
            var pedido = new PagoPedidoDto(v.Id, v.FechaCreacion,
                vendedores.GetValueOrDefault(v.VendedorId, $"Usuario {v.VendedorId}"), v.TotalVenta,
                v.MontoPagado ?? 0, Math.Max(v.TotalVenta - (v.MontoPagado ?? 0), 0),
                v.EstadoPago?.Nombre ?? "Pagado", BuildTransferProofs(v, formasTransferencia));
            return new RevisionPagoRow(v.Cliente?.Nombre ?? $"Cliente {v.ClienteId}", v.Cliente?.NroTelefono,
                pedido, revision?.Estado ?? "PENDIENTE", revision?.Comentario, revision?.UsuarioId, revision?.Fecha,
                revision is null ? null : revisores.GetValueOrDefault(revision.UsuarioId, "Usuario no disponible"));
        }).Where(x => x.Estado == estado).OrderByDescending(x => x.Pedido.Fecha).ToList();
        return Ok(rows);
    }

    private static string NombreRevisor(Usuario usuario)
    {
        var nombre = string.Join(" ", new[] { usuario.Persona?.PrimerNombre, usuario.Persona?.SegundoNombre }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        return string.IsNullOrWhiteSpace(nombre) ? usuario.NombreUsuario ?? "Usuario sin nombre" : nombre;
    }

    [HttpPut("revision-pagos/{pedidoId:int}")]
    public async Task<IActionResult> RevisarPago(int pedidoId, RevisionPagoRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId))
            return Unauthorized();
        if (request.Estado is not ("OK" or "PROBLEMA")) return BadRequest(new { message = "Estado inválido." });
        if (request.Estado == "PROBLEMA" && string.IsNullOrWhiteSpace(request.Comentario))
            return BadRequest(new { message = "Escriba el motivo por el que el pago es incorrecto." });
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
    private static byte[] BuildIncorrectosPdf(IReadOnlyList<RevisionPagoRow> pagos)
    {
        var culture = CultureInfo.GetCultureInfo("es-PY");
        var widths = new[] { 85, 135, 100, 90, 220, 140 };
        var headers = new[] { "Pedido / fecha", "Cliente / vendedor", "Total (Gs.)", "Pagado (Gs.)", "Motivo del pago incorrecto", "Revisión" };
        const int pageLines = 32;
        static string Hex(string value) => Convert.ToHexString(Encoding.Latin1.GetBytes(value));
        static string[] Wrap(string value, int width)
        {
            var result = new List<string>();
            var limit = Math.Max(1, (width - 16) / 5);
            foreach (var paragraph in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var rest = paragraph;
                while (rest.Length > limit)
                {
                    var cut = rest.LastIndexOf(' ', limit);
                    if (cut < 1) cut = limit;
                    result.Add(rest[..cut]);
                    rest = rest[cut..].TrimStart();
                }
                result.Add(rest);
            }
            return result.ToArray();
        }
        var pages = new List<List<(string[][] Cells, int Offset, int Count)>> { new() };
        var used = 0;
        foreach (var pago in pagos)
        {
            var values = new[] { $"#{pago.Pedido.PedidoId}\n{pago.Pedido.Fecha:dd/MM/yyyy}\n{pago.Pedido.Fecha:HH:mm}",
                $"{pago.Cliente}\n\nVendedor:\n{pago.Pedido.Vendedor}",
                pago.Pedido.TotalVenta.ToString("N0", culture), pago.Pedido.MontoPagado.ToString("N0", culture),
                pago.Comentario ?? "Sin comentario", $"{pago.UsuarioNombre}\n\n{pago.FechaRevision:dd/MM/yyyy HH:mm}" };
            var cells = values.Select((value, column) => Wrap(value, widths[column])).ToArray();
            var height = cells.Max(x => x.Length);
            if (height + 1 <= pageLines && used + height + 1 > pageLines) { pages.Add(new()); used = 0; }
            for (var offset = 0; offset < height;)
            {
                var room = pageLines - used - 1;
                if (room < 3) { pages.Add(new()); used = 0; room = pageLines - 1; }
                var count = Math.Min(height - offset, room);
                pages[^1].Add((cells, offset, count));
                used += count + 1;
                offset += count;
                if (offset < height) { pages.Add(new()); used = 0; }
            }
        }
        var objects = new List<string> { "", "",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>" };
        var pageIds = new List<int>();
        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = objects.Count + 1;
            pageIds.Add(pageId);
            var contentId = pageId + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
            var content = new StringBuilder();
            content.AppendLine($"BT /F1 18 Tf 0.05 0.37 0.45 rg 36 552 Td <{Hex("Pagos incorrectos")}> Tj ET");
            content.AppendLine($"BT /F2 8 Tf 0.4 0.45 0.5 rg 36 532 Td <{Hex($"EvaluSystem | Emitido: {DateTime.Now:dd/MM/yyyy HH:mm}")}> Tj ET");
            content.AppendLine($"BT /F1 10 Tf 0.12 0.22 0.28 rg 36 507 Td <{Hex($"{pagos.Count} pagos incorrectos | Total pagado: Gs. {pagos.Sum(x => x.Pedido.MontoPagado).ToString("N0", culture)}")}> Tj ET");
            content.AppendLine("0.05 0.37 0.45 rg 36 465 770 26 re f");
            var x = 36;
            for (var column = 0; column < headers.Length; column++)
            {
                content.AppendLine($"BT /F1 9 Tf 1 1 1 rg {x + 8} 475 Td <{Hex(headers[column])}> Tj ET");
                x += widths[column];
            }
            var top = 465;
            foreach (var row in pages[index])
            {
                var height = (row.Count + 1) * 12;
                x = 36;
                for (var column = 0; column < widths.Length; column++)
                {
                    if (column == 4) content.AppendLine($"0.97 0.98 0.99 rg {x} {top - height} {widths[column]} {height} re f");
                    content.AppendLine($"0.82 0.87 0.9 RG 0.4 w {x} {top - height} {widths[column]} {height} re S");
                    var lines = row.Cells[column].Skip(row.Offset).Take(row.Count).ToArray();
                    if (column == 0 && row.Offset > 0) lines = new[] { row.Cells[0][0], "(continúa)" };
                    for (var line = 0; line < lines.Length; line++)
                    {
                        var alignRight = column is 2 or 3;
                        var textX = alignRight ? x + widths[column] - 8 - lines[line].Length * 4.8 : x + 8;
                        content.AppendLine(FormattableString.Invariant($"BT /F2 8 Tf 0.12 0.22 0.28 rg {textX:0.##} {top - 16 - line * 12} Td <{Hex(lines[line])}> Tj ET"));
                    }
                    x += widths[column];
                }
                top -= height;
            }
            if (pagos.Count == 0) content.AppendLine($"BT /F2 10 Tf 0.4 0.45 0.5 rg 44 440 Td <{Hex("No hay pagos incorrectos para mostrar.")}> Tj ET");
            content.AppendLine($"BT /F2 8 Tf 0.4 0.45 0.5 rg 36 28 Td <{Hex($"Página {index + 1} de {pages.Count}")}> Tj ET");
            var streamContent = content.ToString();
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(streamContent)} >>\nstream\n{streamContent}endstream");
        }
        objects[0] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[1] = $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] >>";
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, 1024, leaveOpen: true);
        writer.Write("%PDF-1.4\n");
        writer.Flush();
        var offsets = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            writer.Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            writer.Flush();
        }
        var xref = output.Position;
        writer.Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) writer.Write($"{offset:0000000000} 00000 n \n");
        writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        writer.Flush();
        return output.ToArray();
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

    private static string NombrePersona(Persona persona)
    {
        var parts = new[] { persona.PrimerNombre, persona.SegundoNombre, persona.PrimerApellido, persona.SegundoApellido }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var nombre = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(nombre) ? $"Persona {persona.Id}" : nombre;
    }
}
