using System.Net;
using System.Security.Claims;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImpresionesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    private readonly IPermisoService _permisoService;
    private readonly IConfiguracionService _configuracionService;
    private readonly IEstadoVentaFlujoService _estadoVentaFlujoService;

    public ImpresionesController(EvaluSystemDbContext context, IPermisoService permisoService, IConfiguracionService configuracionService, IEstadoVentaFlujoService estadoVentaFlujoService)
    {
        _context = context;
        _permisoService = permisoService;
        _configuracionService = configuracionService;
        _estadoVentaFlujoService = estadoVentaFlujoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ImpresionArchivoDto>>> GetAll(
        [FromQuery] DateTime? fechaDesde,
        [FromQuery] DateTime? fechaHasta,
        [FromQuery] int? maquinaId,
        CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("ver"))
        {
            return Forbid();
        }

        var desde = fechaDesde?.Date;
        var hasta = fechaHasta?.Date.AddDays(1);

        var query = _context.VentasImpresionDet
            .Include(x => x.Cabecera).ThenInclude(x => x!.Cliente)
            .Include(x => x.Cabecera).ThenInclude(x => x!.EstadoVenta)
            .Include(x => x.Producto)
            .Include(x => x.TipoMaquina)
            .AsNoTracking()
            .Where(x => x.CheckImpresion != true)
            .Where(x => !string.IsNullOrWhiteSpace(x.ArchivoDisenio) || !string.IsNullOrWhiteSpace(x.ArchivoDisenioNombre));

        if (desde.HasValue)
        {
            query = query.Where(x => x.Cabecera != null && x.Cabecera.FechaCreacion >= desde.Value);
        }

        if (hasta.HasValue)
        {
            query = query.Where(x => x.Cabecera != null && x.Cabecera.FechaCreacion < hasta.Value);
        }

        if (maquinaId.HasValue)
        {
            query = query.Where(x => x.TipoMaquinaId == maquinaId.Value);
        }

        var items = await query
            .OrderBy(x => x.TipoMaquina!.Nombre)
            .ThenBy(x => x.Cabecera!.FechaEntrega ?? x.Cabecera!.FechaCreacion)
            .ThenBy(x => x.CabId)
            .ThenBy(x => x.Id)
            .Take(500)
            .Select(x => new ImpresionArchivoDto(
                x.Id,
                x.CabId,
                x.Cabecera!.FechaCreacion,
                x.Cabecera.FechaEntrega,
                x.Cabecera.Cliente != null ? x.Cabecera.Cliente.Nombre ?? string.Empty : string.Empty,
                x.TipoMaquinaId,
                x.TipoMaquina != null ? x.TipoMaquina.Nombre : "Sin maquina",
                x.Producto != null ? x.Producto.Nombre : "Sin producto",
                x.Cantidad,
                x.ArchivoDisenioNombre,
                x.Cabecera.EstadoVenta != null ? x.Cabecera.EstadoVenta.Nombre ?? x.Cabecera.EstadoVentaId : x.Cabecera.EstadoVentaId,
                x.CheckImpresion == true))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{detalleId:int}/archivo")]
    public async Task<ActionResult<ExcelFileDto>> DescargarArchivo(int detalleId, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("ver"))
        {
            return Forbid();
        }

        var detalle = await _context.VentasImpresionDet
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == detalleId, cancellationToken);

        if (detalle is null)
        {
            return NotFound(new { message = "No se encontro el archivo." });
        }

        if (string.IsNullOrWhiteSpace(detalle.ArchivoDisenio))
        {
            return BadRequest(new { message = "El detalle no tiene archivo cargado." });
        }

        var fileName = string.IsNullOrWhiteSpace(detalle.ArchivoDisenioNombre)
            ? $"pedido-{detalle.CabId}-detalle-{detalle.Id}.bin"
            : detalle.ArchivoDisenioNombre;
        var contentType = GuessContentType(fileName);

        if (TryExtractBase64(detalle.ArchivoDisenio, out var extracted))
        {
            return Ok(new ExcelFileDto(fileName, contentType, extracted));
        }

        var basePath = await GetBasePathAsync();
        var safeFilePath = ResolveSafeFilePath(basePath, detalle.ArchivoDisenio);
        if (safeFilePath is null)
        {
            return BadRequest(new { message = "La ruta del archivo no es valida." });
        }

        if (!System.IO.File.Exists(safeFilePath))
        {
            return NotFound(new { message = "El archivo del diseno no existe en el servidor." });
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(safeFilePath, cancellationToken);
        return Ok(new ExcelFileDto(fileName, contentType, Convert.ToBase64String(bytes)));
    }

    [HttpPut("{detalleId:int}/marcar-impreso")]
    public async Task<ActionResult<ImpresionMarcarDto>> MarcarImpreso(int detalleId, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("editar"))
        {
            return Forbid();
        }

        var detalle = await _context.VentasImpresionDet
            .Include(x => x.Cabecera)
            .ThenInclude(x => x!.EstadoVenta)
            .FirstOrDefaultAsync(x => x.Id == detalleId, cancellationToken);

        if (detalle is null || detalle.Cabecera is null)
        {
            return NotFound(new { message = "No se encontro el detalle de impresion." });
        }

        detalle.CheckImpresion = true;

        var detallesPedido = await _context.VentasImpresionDet
            .Where(x => x.CabId == detalle.CabId)
            .ToListAsync(cancellationToken);
        var pedidoCompleto = detallesPedido.All(x => x.Id == detalle.Id || x.CheckImpresion == true);

        if (pedidoCompleto)
        {
            var siguienteEstado = await _estadoVentaFlujoService.ObtenerSiguienteAsync(detalle.Cabecera.EstadoVenta, cancellationToken);

            if (siguienteEstado is not null)
            {
                detalle.Cabecera.EstadoVentaId = siguienteEstado.Id;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var estadoVenta = await _context.EstadosVenta
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == detalle.Cabecera.EstadoVentaId, cancellationToken);

        return Ok(new ImpresionMarcarDto(
            detalle.Id,
            detalle.CabId,
            true,
            pedidoCompleto,
            detalle.Cabecera.EstadoVentaId,
            estadoVenta?.Nombre ?? detalle.Cabecera.EstadoVentaId));
    }

    private async Task<string> GetBasePathAsync()
    {
        var basePath = await _configuracionService.ObtenerValorAsync("RUTA_DE_ARCHIVOS", 1)
            ?? await _configuracionService.ObtenerValorAsync("FileStoragePath", 1);
        return string.IsNullOrWhiteSpace(basePath)
            ? Path.Combine(AppContext.BaseDirectory, "Archivos")
            : basePath;
    }

    private static string? ResolveSafeFilePath(string basePath, string requestedPath)
    {
        var fullBasePath = Path.GetFullPath(basePath);
        var fullRequestedPath = Path.GetFullPath(requestedPath);

        var normalizedBasePath = fullBasePath.EndsWith(Path.DirectorySeparatorChar)
            ? fullBasePath
            : fullBasePath + Path.DirectorySeparatorChar;

        return fullRequestedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase)
            ? fullRequestedPath
            : null;
    }

    private async Task<bool> TienePermisoAsync(string accion)
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId)
            && await _permisoService.UsuarioTienePermisoAsync(userId, "Impresiones", accion);
    }

    private static bool TryExtractBase64(string value, out string bytes)
    {
        bytes = value;
        var commaIndex = value.IndexOf(',');
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
        {
            bytes = value[(commaIndex + 1)..];
            return true;
        }

        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GuessContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".ai" => "application/postscript",
            ".psd" => "image/vnd.adobe.photoshop",
            ".cdr" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }
}
