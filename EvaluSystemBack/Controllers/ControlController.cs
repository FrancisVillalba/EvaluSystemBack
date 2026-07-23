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
    private const string EstadoDetalleAprobado = "AP";
    private const string EstadoDetalleRechazado = "RE";

    private readonly EvaluSystemDbContext _context;
    private readonly IPermisoService _permisoService;
    private readonly IEstadoVentaFlujoService _estadoVentaFlujoService;
    private readonly IConfiguracionService _configuracionService;

    public ControlController(
        EvaluSystemDbContext context,
        IPermisoService permisoService,
        IEstadoVentaFlujoService estadoVentaFlujoService,
        IConfiguracionService configuracionService)
    {
        _context = context;
        _permisoService = permisoService;
        _estadoVentaFlujoService = estadoVentaFlujoService;
        _configuracionService = configuracionService;
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
            .Where(x => x.Detalles.Any(d => d.EstadoItem != EstadoDetalleAprobado && d.EstadoItem != EstadoDetalleRechazado))
            .OrderBy(x => x.Detalles.Min(d => d.TipoMaquina!.Nombre))
            .ThenBy(x => x.FechaEntrega ?? x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(300)
            .ToListAsync(cancellationToken);

        return Ok(pedidos.Select(ToDto));
    }


    [HttpGet("{detalleId:int}/archivo")]
    public async Task<ActionResult<ExcelFileDto>> DescargarArchivo(int detalleId, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("ver"))
        {
            return Forbid();
        }

        var estadoControl = await _estadoVentaFlujoService.ObtenerPorIdAsync("CO", cancellationToken);
        var detalle = await _context.VentasImpresionDet
            .Include(x => x.Cabecera)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == detalleId, cancellationToken);

        if (detalle is null || detalle.Cabecera is null)
        {
            return NotFound(new { message = "No se encontro el archivo." });
        }

        if (estadoControl is not null && detalle.Cabecera.EstadoVentaId != estadoControl.Id)
        {
            return BadRequest(new { message = "El archivo no esta en control." });
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
    [HttpPost("{id:int}/aprobar")]
    public async Task<ActionResult<ControlPedidoDto>> Aprobar(int id, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("editar"))
        {
            return Forbid();
        }

        var detalle = await QueryDetalleForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (detalle is null || detalle.Cabecera is null)
        {
            return NotFound(new { message = "No se encontro el detalle." });
        }

        var estadoControl = await _estadoVentaFlujoService.ObtenerPorIdAsync("CO", cancellationToken);
        if (estadoControl is not null && detalle.Cabecera.EstadoVentaId != estadoControl.Id)
        {
            return BadRequest(new { message = "El pedido no esta en control." });
        }

        if (EstadoItemControlado(detalle.EstadoItem))
        {
            return BadRequest(new { message = "El detalle ya fue controlado." });
        }

        var userId = CurrentUserId();
        detalle.EstadoItem = EstadoDetalleAprobado;
        detalle.FechaModificacion = DateTime.Now;
        detalle.UsuModificacion = userId ?? detalle.UsuModificacion;

        var updateError = await ActualizarCabeceraDespuesDeControlAsync(detalle.Cabecera, userId, cancellationToken);
        if (updateError is not null)
        {
            return BadRequest(new { message = updateError });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var updated = await Query().FirstAsync(x => x.Id == detalle.CabId, cancellationToken);
        return Ok(ToDto(updated));
    }


    [HttpPost("{id:int}/rechazar")]
    public async Task<ActionResult<ControlPedidoDto>> Rechazar(int id, [FromBody] EliminarVentaImpresionRequest request, CancellationToken cancellationToken)
    {
        if (!await TienePermisoAsync("editar"))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Observacion))
        {
            return BadRequest(new { message = "Debe agregar un comentario para rechazar el detalle." });
        }

        var detalle = await QueryDetalleForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (detalle is null || detalle.Cabecera is null)
        {
            return NotFound(new { message = "No se encontro el detalle." });
        }

        var estadoControl = await _estadoVentaFlujoService.ObtenerPorIdAsync("CO", cancellationToken);
        if (estadoControl is not null && detalle.Cabecera.EstadoVentaId != estadoControl.Id)
        {
            return BadRequest(new { message = "El pedido no esta en control." });
        }

        if (EstadoItemControlado(detalle.EstadoItem))
        {
            return BadRequest(new { message = "El detalle ya fue controlado." });
        }

        var userId = CurrentUserId();
        var observacion = request.Observacion.Trim();
        detalle.EstadoItem = EstadoDetalleRechazado;
        detalle.Observacion = observacion;
        detalle.FechaModificacion = DateTime.Now;
        detalle.UsuModificacion = userId ?? detalle.UsuModificacion;

        detalle.Cabecera.Observacion = observacion;

        var updateError = await ActualizarCabeceraDespuesDeControlAsync(detalle.Cabecera, userId, cancellationToken);
        if (updateError is not null)
        {
            return BadRequest(new { message = updateError });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var updated = await Query().FirstAsync(x => x.Id == detalle.CabId, cancellationToken);
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


    private IQueryable<VentaImpresionDet> QueryDetalleForUpdate()
    {
        return _context.VentasImpresionDet
            .Include(x => x.Cabecera).ThenInclude(x => x!.EstadoVenta)
            .Include(x => x.Cabecera).ThenInclude(x => x!.Detalles);
    }

    private async Task<string?> ActualizarCabeceraDespuesDeControlAsync(VentaImpresionCab pedido, int? userId, CancellationToken cancellationToken)
    {
        if (!pedido.Detalles.All(x => EstadoItemControlado(x.EstadoItem)))
        {
            return null;
        }

        if (pedido.Detalles.All(x => EsEstadoItem(x.EstadoItem, EstadoDetalleRechazado)))
        {
            var estadoEliminado = await _estadoVentaFlujoService.ObtenerPorIdAsync("XX", cancellationToken);
            if (estadoEliminado is null)
            {
                return "No existe el estado eliminado configurado.";
            }

            pedido.EstadoVentaId = estadoEliminado.Id;
        }
        else
        {
            var siguienteEstado = await _estadoVentaFlujoService.ObtenerSiguienteAsync(pedido.EstadoVenta, cancellationToken);
            if (siguienteEstado is null)
            {
                return "No existe un siguiente estado de venta configurado.";
            }

            pedido.EstadoVentaId = siguienteEstado.Id;
        }

        pedido.FechaModificacion = DateTime.Now;
        pedido.UsuModificacion = userId ?? pedido.UsuModificacion;
        return null;
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


    private static bool EstadoItemControlado(string? estadoItem)
    {
        return EsEstadoItem(estadoItem, EstadoDetalleAprobado) || EsEstadoItem(estadoItem, EstadoDetalleRechazado);
    }

    private static bool EsEstadoItem(string? estadoItem, string esperado)
    {
        return string.Equals((estadoItem ?? string.Empty).Trim(), esperado, StringComparison.OrdinalIgnoreCase);
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
                .Where(x => !EstadoItemControlado(x.EstadoItem))
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
            detalle.EstadoItem,
            detalle.CheckImpresion == true);
    }
}