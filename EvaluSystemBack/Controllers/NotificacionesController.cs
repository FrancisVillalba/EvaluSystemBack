using System.Security.Claims;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Security;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[SkipPermission]
public class NotificacionesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    private readonly INotificacionService _service;
    public NotificacionesController(EvaluSystemDbContext context, INotificacionService service) { _context = context; _service = service; }

    [HttpGet]
    public async Task<ActionResult<NotificacionesResumenDto>> Get(CancellationToken cancellationToken)
    {
        if (!TryUsuarioId(out var usuarioId)) return Unauthorized();
        await _service.AsegurarTablaAsync(cancellationToken);
        var items = await _context.Notificaciones.AsNoTracking().Where(x => x.UsuarioId == usuarioId && _context.VentasImpresionCab.Any(p => p.Id == x.PedidoId && p.VendedorId == usuarioId))
            .OrderByDescending(x => x.FechaCreacion).Take(30)
            .Select(x => new NotificacionDto(x.Id, x.Tipo, x.Titulo, x.Mensaje, x.PedidoId, x.DetalleId, x.Producto, x.Comentario, x.Leida, x.FechaCreacion, x.FechaLectura))
            .ToListAsync(cancellationToken);
        var noLeidas = await _context.Notificaciones.CountAsync(x => x.UsuarioId == usuarioId && !x.Leida && _context.VentasImpresionCab.Any(p => p.Id == x.PedidoId && p.VendedorId == usuarioId), cancellationToken);
        return Ok(new NotificacionesResumenDto(noLeidas, items));
    }

    [HttpPut("{id:long}/leer")]
    public async Task<IActionResult> Leer(long id, CancellationToken cancellationToken)
    {
        if (!TryUsuarioId(out var usuarioId)) return Unauthorized();
        await _service.AsegurarTablaAsync(cancellationToken);
        var item = await _context.Notificaciones.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuarioId && _context.VentasImpresionCab.Any(p => p.Id == x.PedidoId && p.VendedorId == usuarioId), cancellationToken);
        if (item is null) return NotFound();
        item.Leida = true; item.FechaLectura = DateTime.Now;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("leer-todas")]
    public async Task<IActionResult> LeerTodas(CancellationToken cancellationToken)
    {
        if (!TryUsuarioId(out var usuarioId)) return Unauthorized();
        await _service.AsegurarTablaAsync(cancellationToken);
        await _context.Notificaciones.Where(x => x.UsuarioId == usuarioId && !x.Leida && _context.VentasImpresionCab.Any(p => p.Id == x.PedidoId && p.VendedorId == usuarioId))
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Leida, true).SetProperty(n => n.FechaLectura, DateTime.Now), cancellationToken);
        return NoContent();
    }

    private bool TryUsuarioId(out int usuarioId)
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out usuarioId);
    }
}