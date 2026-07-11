using System.Security.Claims;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[SkipPermission]
public class MensajesController : ControllerBase
{
    private const int DiasMoraPedidoDefault = 7;
    private const string ConfigDiasAtrasoSaldoPendiente = "DIAS_ATRASO_SALDO_PENDIENTE";
    private const string ConfigDiasAvisoSaldoPendiente = "DiasAvisoSaldoPendiente";
    private const int FlujoEliminado = 5;
    private readonly EvaluSystemDbContext _context;

    public MensajesController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet("pendientes")]
    public async Task<ActionResult<IEnumerable<MensajePendienteDto>>> GetPendientes(CancellationToken cancellationToken)
    {
        var usuarioId = CurrentUserId();
        if (!usuarioId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        var mensajes = new List<MensajePendienteDto>();
        mensajes.AddRange(await BuildCumpleaniosAsync(usuarioId.Value, cancellationToken));
        mensajes.AddRange(await BuildPagosPendientesAsync(usuarioId.Value, cancellationToken));

        var claves = mensajes.Select(x => x.Clave).ToList();
        var aceptados = await _context.UsuarioMensajesAceptados
            .AsNoTracking()
            .Where(x => x.UsuarioId == usuarioId.Value && claves.Contains(x.Clave))
            .Select(x => x.Clave)
            .ToListAsync(cancellationToken);

        return Ok(mensajes
            .Where(x => !aceptados.Contains(x.Clave))
            .OrderBy(x => x.Tipo)
            .ThenBy(x => x.Titulo));
    }

    [HttpPost("{clave}/aceptar")]
    public async Task<IActionResult> Aceptar(string clave, CancellationToken cancellationToken)
    {
        var usuarioId = CurrentUserId();
        if (!usuarioId.HasValue)
        {
            return Unauthorized(new { message = "No se pudo identificar el usuario logueado." });
        }

        clave = Uri.UnescapeDataString(clave);
        if (string.IsNullOrWhiteSpace(clave))
        {
            return BadRequest(new { message = "La clave del mensaje es obligatoria." });
        }

        var exists = await _context.UsuarioMensajesAceptados
            .AnyAsync(x => x.UsuarioId == usuarioId.Value && x.Clave == clave, cancellationToken);

        if (!exists)
        {
            _context.UsuarioMensajesAceptados.Add(new UsuarioMensajeAceptado
            {
                UsuarioId = usuarioId.Value,
                Clave = clave,
                FechaAceptado = DateTime.Now
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<IEnumerable<MensajePendienteDto>> BuildCumpleaniosAsync(int usuarioId, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var cumpleanieros = await _context.Personas
            .AsNoTracking()
            .Where(x => x.Estado != false && x.FechaCumpleanios.HasValue)
            .Where(x => x.FechaCumpleanios!.Value.Month == today.Month && x.FechaCumpleanios.Value.Day == today.Day)
            .OrderBy(x => x.PrimerNombre)
            .ThenBy(x => x.PrimerApellido)
            .ToListAsync(cancellationToken);

        return cumpleanieros.Select(persona =>
        {
            var nombre = NombrePersona(persona);
            return new MensajePendienteDto(
                $"cumple:{today:yyyy}:{persona.Id}",
                "Cumpleaños del día",
                $"Hoy es el cumpleaños de {nombre}. No te olvides de felicitarle.",
                "cumpleanios");
        });
    }

    private async Task<IEnumerable<MensajePendienteDto>> BuildPagosPendientesAsync(int usuarioId, CancellationToken cancellationToken)
    {
        var diasMoraPedido = await GetDiasMoraPedidoAsync(cancellationToken);
        var limite = DateTime.Today.AddDays(-diasMoraPedido);

        var query = _context.VentasImpresionCab
            .AsNoTracking()
            .Include(x => x.Cliente)
            .Include(x => x.EstadoVenta)
            .Where(x => x.FechaCreacion.Date <= limite)
            .Where(x => x.TotalVenta > (x.MontoPagado ?? 0))
            .Where(x => x.EstadoVenta == null || x.EstadoVenta.NumeroFlujo != FlujoEliminado);
        query = query.Where(x => x.VendedorId == usuarioId);

        var pedidos = await query
            .OrderBy(x => x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        return pedidos.Select(pedido =>
        {
            var saldo = pedido.TotalVenta - (pedido.MontoPagado ?? 0);
            var cliente = string.IsNullOrWhiteSpace(pedido.Cliente?.Nombre) ? $"Cliente {pedido.ClienteId}" : pedido.Cliente!.Nombre!;
            return new MensajePendienteDto(
                $"saldo-pendiente:{pedido.Id}",
                "Cliente con saldo pendiente",
                $"El cliente {cliente} tiene saldo pendiente del pedido #{pedido.Id} hace mas de {diasMoraPedido} dias. Saldo: Gs. {saldo:N0}.",
                "pago");
        });
    }

    private async Task<int> GetDiasMoraPedidoAsync(CancellationToken cancellationToken)
    {
        var valor = await _context.Configuraciones
            .AsNoTracking()
            .Where(x => (x.Nombre == ConfigDiasAtrasoSaldoPendiente || x.Nombre == ConfigDiasAvisoSaldoPendiente) && x.NroConfiguracion == 1)
            .OrderBy(x => x.Nombre == ConfigDiasAtrasoSaldoPendiente ? 0 : 1)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync(cancellationToken);

        return int.TryParse(valor, out var dias) && dias > 0 ? dias : DiasMoraPedidoDefault;
    }

    private async Task<bool> IsAdminAsync(int usuarioId, CancellationToken cancellationToken)
    {
        return await _context.UsuarioPerfiles
            .AsNoTracking()
            .Include(x => x.Perfil)
            .AnyAsync(x => x.UsuarioId == usuarioId && x.Estado && x.Perfil != null && x.Perfil.Nombre == "Administrador", cancellationToken);
    }

    private int? CurrentUserId()
    {
        var value = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private static string NombrePersona(Persona persona)
    {
        var nombre = string.Join(" ", new[]
        {
            persona.PrimerNombre,
            persona.SegundoNombre,
            persona.PrimerApellido,
            persona.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(nombre) ? $"Usuario {persona.Id}" : nombre;
    }
}
