using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GruposVentaController : ControllerBase
{
    private static readonly HashSet<string> EstadosVentaComisionables = new(StringComparer.OrdinalIgnoreCase) { "CO", "EE", "PE", "PI" };

    private readonly EvaluSystemDbContext _context;

    public GruposVentaController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GrupoVentaDto>>> GetAll()
    {
        var query = await FilterGroupsForCurrentUserAsync(Query());
        var items = await query
            .OrderBy(x => x.Nombre)
            .ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GrupoVentaDto>> GetById(int id)
    {
        var query = await FilterGroupsForCurrentUserAsync(Query());
        var item = await query.FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpGet("mi-equipo")]
    public async Task<ActionResult<GrupoVentaEquipoDto>> GetMiEquipo(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null)
    {
        return Ok(await BuildMiEquipoAsync(dateFrom, dateTo, vendedorId));
    }

    [HttpGet("mi-equipo/excel")]
    public async Task<ActionResult<ExcelFileDto>> ExportMiEquipoExcel(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? vendedorId = null)
    {
        var report = await BuildMiEquipoAsync(dateFrom, dateTo, vendedorId);
        var bytes = BuildMiEquipoXlsx(report);
        return Ok(new ExcelFileDto(
            $"grupo-ventas-{report.FechaDesde:yyyyMMdd}-{report.FechaHasta:yyyyMMdd}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Convert.ToBase64String(bytes)));
    }

    private async Task<GrupoVentaEquipoDto> BuildMiEquipoAsync(DateTime? dateFrom = null, DateTime? dateTo = null, int? vendedorId = null)
    {
        var usuarioId = CurrentUserId();
        if (!usuarioId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);

        var isAdmin = await UserHasProfileAsync(usuarioId.Value, "Administrador");
        var isTeamLeader = await UserHasProfileAsync(usuarioId.Value, "Team Leader");
        var isExternalSeller = await UserHasProfileAsync(usuarioId.Value, "Venta Externa");
        var canFilterSellers = !isExternalSeller || isTeamLeader || isAdmin;
        HashSet<int> vendedorIds;
        Dictionary<int, string> vendedores;

        if (isExternalSeller && !isTeamLeader && !isAdmin)
        {
            vendedorIds = new HashSet<int> { usuarioId.Value };
            var usuario = await _context.Usuarios
                .Include(x => x.Persona)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == usuarioId.Value);
            vendedores = new Dictionary<int, string>
            {
                [usuarioId.Value] = usuario is null ? $"Usuario {usuarioId.Value}" : NombreUsuario(usuario)
            };
        }
        else
        {
            var gruposQuery = _context.GruposVenta
                .Include(x => x.Vendedores).ThenInclude(x => x.VendedorUsuario).ThenInclude(x => x.Persona)
                .AsNoTracking()
                .Where(x => x.Estado);

            if (!isAdmin)
            {
                gruposQuery = gruposQuery.Where(x => x.TeamLeaderUsuarioId == usuarioId.Value);
            }

            var grupos = await gruposQuery.ToListAsync();
            if (grupos.Count == 0)
            {
                return new GrupoVentaEquipoDto(from, to, [], [], [], canFilterSellers);
            }

            var vendedoresActivos = grupos
                .SelectMany(x => x.Vendedores)
                .Where(x => x.Estado)
                .GroupBy(x => x.VendedorUsuarioId)
                .Select(x => x.First())
                .ToList();
            vendedorIds = vendedoresActivos.Select(x => x.VendedorUsuarioId).ToHashSet();

            if (vendedorIds.Count == 0)
            {
                return new GrupoVentaEquipoDto(from, to, [], [], [], canFilterSellers);
            }

            vendedores = vendedoresActivos.ToDictionary(
                x => x.VendedorUsuarioId,
                x => x.VendedorUsuario is null ? $"Usuario {x.VendedorUsuarioId}" : NombreUsuario(x.VendedorUsuario));
        }

        var vendedoresFiltro = vendedores
            .Select(x => new GrupoVentaFiltroVendedorDto(x.Key, x.Value))
            .OrderBy(x => x.Vendedor)
            .ToList();

        if (isExternalSeller && !isTeamLeader && !isAdmin)
        {
            vendedorId = usuarioId.Value;
        }
        else if (vendedorId.HasValue)
        {
            if (!vendedorIds.Contains(vendedorId.Value))
            {
                return new GrupoVentaEquipoDto(from, to, [], [], vendedoresFiltro, canFilterSellers);
            }

            vendedorIds = new HashSet<int> { vendedorId.Value };
        }

        var ventas = await _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles)
            .AsNoTracking()
            .Where(x => vendedorIds.Contains(x.VendedorId))
            .Where(x => x.FechaCreacion >= from && x.FechaCreacion < toExclusive)
            .OrderByDescending(x => x.FechaCreacion)
            .ToListAsync();

        ventas = ventas
            .Where(x => x.EstadoVenta?.Nombre?.Contains("elimin", StringComparison.OrdinalIgnoreCase) != true)
            .ToList();

        var comisiones = await _context.ProductoComisiones
            .AsNoTracking()
            .Where(x => x.Estado)
            .ToListAsync();
        var teamLeaderPerfilId = await ProfileIdAsync("Team Leader");
        var externalSellerPerfilId = await ProfileIdAsync("Venta Externa");
        var commissionPerfilId = isTeamLeader && !isAdmin ? teamLeaderPerfilId : externalSellerPerfilId;
        var includeExtraInCommission = !(isTeamLeader && !isAdmin);

        var resumen = ventas
            .GroupBy(x => x.VendedorId)
            .Select(x => new GrupoVentaResumenVendedorDto(
                x.Key,
                vendedores.GetValueOrDefault(x.Key, $"Usuario {x.Key}"),
                x.Count(),
                x.Sum(item => item.TotalVenta),
                x.Sum(item => item.Detalles.Sum(detail => detail.Cantidad)),
                x.Sum(item => CalculateCommission(
                    item,
                    commissionPerfilId,
                    includeExtra: includeExtraInCommission,
                    comisiones))))
            .OrderByDescending(x => x.TotalVenta)
            .ToList();
        var detalle = ventas.Select(x => new GrupoVentaVentaDto(
                x.Id,
                x.FechaCreacion,
                x.VendedorId,
                vendedores.GetValueOrDefault(x.VendedorId, $"Usuario {x.VendedorId}"),
                x.Cliente?.Nombre ?? string.Empty,
                x.EstadoVenta?.Nombre ?? x.EstadoVentaId,
                x.TotalVenta,
                x.Detalles.Sum(detail => detail.Cantidad),
                CalculateCommission(
                    x,
                    commissionPerfilId,
                    includeExtra: includeExtraInCommission,
                    comisiones)))
            .ToList();

        return new GrupoVentaEquipoDto(from, to, resumen, detalle, vendedoresFiltro, canFilterSellers);
    }

    [HttpPost]
    public async Task<ActionResult<GrupoVentaDto>> Create(GrupoVentaRequest request)
    {
        var validation = await ValidateRequestAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        var item = request.ToEntity();
        SetVendedores(item, request.VendedorUsuarioIds);
        _context.GruposVenta.Add(item);
        await _context.SaveChangesAsync();

        var created = await Query().FirstAsync(x => x.Id == item.Id);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, created.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GrupoVentaRequest request)
    {
        var item = await _context.GruposVenta.Include(x => x.Vendedores).FirstOrDefaultAsync(x => x.Id == id);
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
        SetVendedores(item, request.VendedorUsuarioIds);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.GruposVenta.Include(x => x.Vendedores).FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        _context.GrupoVentaVendedores.RemoveRange(item.Vendedores);
        _context.GruposVenta.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Models.GrupoVenta> Query()
    {
        return _context.GruposVenta
            .Include(x => x.TeamLeaderUsuario).ThenInclude(x => x.Persona)
            .Include(x => x.Vendedores).ThenInclude(x => x.VendedorUsuario).ThenInclude(x => x.Persona)
            .AsNoTracking();
    }

    private async Task<IQueryable<Models.GrupoVenta>> FilterGroupsForCurrentUserAsync(IQueryable<Models.GrupoVenta> query)
    {
        var usuarioId = CurrentUserId();
        if (!usuarioId.HasValue)
        {
            return query.Where(x => false);
        }

        if (await UserHasProfileAsync(usuarioId.Value, "Administrador"))
        {
            return query;
        }

        if (await UserHasProfileAsync(usuarioId.Value, "Team Leader"))
        {
            return query.Where(x => x.TeamLeaderUsuarioId == usuarioId.Value);
        }

        return query.Where(x => x.Vendedores.Any(v => v.Estado && v.VendedorUsuarioId == usuarioId.Value));
    }

    private async Task<ActionResult?> ValidateRequestAsync(GrupoVentaRequest request, int? id = null)
    {
        if (!await UserHasProfileAsync(request.TeamLeaderUsuarioId, "Team Leader"))
        {
            return BadRequest(new { message = "El usuario seleccionado debe tener perfil Team Leader." });
        }

        var vendedorIds = (request.VendedorUsuarioIds ?? Array.Empty<int>()).Distinct().ToList();
        foreach (var vendedorId in vendedorIds)
        {
            if (!await UserHasProfileAsync(vendedorId, "Venta Externa"))
            {
                return BadRequest(new { message = "Todos los vendedores deben tener perfil Venta Externa." });
            }
        }

        var repeated = await _context.GrupoVentaVendedores
            .Include(x => x.GrupoVenta)
            .Where(x => x.Estado && x.GrupoVenta.Estado)
            .Where(x => !id.HasValue || x.GrupoVentaId != id.Value)
            .Where(x => vendedorIds.Contains(x.VendedorUsuarioId))
            .Select(x => x.VendedorUsuarioId)
            .FirstOrDefaultAsync();

        return repeated > 0
            ? BadRequest(new { message = "Un vendedor externo ya pertenece a otro grupo activo." })
            : null;
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

    private static void SetVendedores(Models.GrupoVenta item, IEnumerable<int>? vendedorUsuarioIds)
    {
        var requested = (vendedorUsuarioIds ?? Array.Empty<int>()).Distinct().ToHashSet();

        foreach (var current in item.Vendedores)
        {
            current.Estado = requested.Contains(current.VendedorUsuarioId);
        }

        var currentIds = item.Vendedores.Select(x => x.VendedorUsuarioId).ToHashSet();
        foreach (var vendedorId in requested.Where(x => !currentIds.Contains(x)))
        {
            item.Vendedores.Add(new Models.GrupoVentaVendedor
            {
                VendedorUsuarioId = vendedorId,
                Estado = true
            });
        }
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private static string NombreUsuario(Models.Usuario usuario)
    {
        if (usuario.Persona is null)
        {
            return usuario.NombreUsuario ?? $"Usuario {usuario.Id}";
        }

        var nombre = string.Join(" ", new[]
        {
            usuario.Persona.PrimerNombre,
            usuario.Persona.SegundoNombre,
            usuario.Persona.PrimerApellido,
            usuario.Persona.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(nombre) ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}" : nombre;
    }

    private static decimal CalculateCommission(
        Models.VentaImpresionCab venta,
        int perfilComisionId,
        bool includeExtra,
        IReadOnlyCollection<Models.ProductoComision> comisiones)
    {
        if (!EstadosVentaComisionables.Contains(venta.EstadoVentaId))
        {
            return 0;
        }

        return venta.Detalles.Where(EsDetalleComisionable).Sum(detalle =>
        {
            var comisionUnitario = ResolveCommission(detalle.ProductoId, perfilComisionId, venta.FechaCreacion, comisiones);
            return detalle.Cantidad * comisionUnitario + (includeExtra ? detalle.PrecioExtra ?? 0 : 0);
        });
    }

    private static bool EsDetalleComisionable(Models.VentaImpresionDet detalle)
    {
        return !string.Equals((detalle.EstadoItem ?? string.Empty).Trim(), "RE", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ResolveCommission(
        int productoId,
        int perfilComisionId,
        DateTime fecha,
        IReadOnlyCollection<Models.ProductoComision> comisiones)
    {
        if (perfilComisionId <= 0)
        {
            return 0;
        }

        return comisiones
            .Where(x => x.ProductoId == productoId && x.PerfilId == perfilComisionId)
            .Where(x => !x.FechaDesde.HasValue || x.FechaDesde.Value.Date <= fecha.Date)
            .Where(x => !x.FechaHasta.HasValue || x.FechaHasta.Value.Date >= fecha.Date)
            .OrderByDescending(x => x.FechaDesde ?? DateTime.MinValue)
            .Select(x => x.MontoPorMetro)
            .FirstOrDefault();
    }

    private static byte[] BuildMiEquipoXlsx(GrupoVentaEquipoDto report)
    {
        var rows = new List<string[]>
        {
            new[] { "Pedido", "Fecha", "Vendedor", "Cliente", "Estado", "Total venta", "Metros", "Total comision" }
        };

        rows.AddRange(report.Ventas.Select(item => new[]
        {
            item.PedidoId.ToString(CultureInfo.InvariantCulture),
            item.Fecha.ToString("yyyy-MM-dd"),
            item.Vendedor,
            item.Cliente,
            item.Estado,
            Money(item.TotalVenta),
            item.TotalMetros.ToString("N2", CultureInfo.CurrentCulture),
            Money(item.TotalComision)
        }));

        rows.Add(new[]
        {
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "Total comision",
            Money(report.Ventas.Sum(x => x.TotalComision))
        });

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
                    <sheets><sheet name="Grupo de ventas" sheetId="1" r:id="rId1"/></sheets>
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

                if (rowIndex == 0 || rowIndex == rows.Count - 1)
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

    private static string Money(decimal value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
