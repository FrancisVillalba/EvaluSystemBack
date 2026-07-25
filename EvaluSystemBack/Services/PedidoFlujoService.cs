using System.Security.Claims;
using System.Text.Json;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class PedidoFlujoService : IPedidoFlujoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EvaluSystemDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PedidoFlujoService(EvaluSystemDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task RegistrarAsync(
        VentaImpresionCab pedido,
        string accion,
        string? estadoAnteriorId,
        string? estadoNuevoId,
        string? comentario = null,
        int? detalleId = null,
        int? usuarioId = null,
        CancellationToken cancellationToken = default,
        string? producto = null)
    {
        usuarioId ??= CurrentUserId();
        var usuario = await NombreUsuarioAsync(usuarioId, cancellationToken);
        var estados = await NombresEstadosAsync(estadoAnteriorId, estadoNuevoId, cancellationToken);
        var eventos = Obtener(pedido).ToList();

        eventos.Add(new PedidoFlujoEventoDto(
            DateTime.Now,
            usuarioId,
            usuario,
            accion,
            estadoAnteriorId ?? string.Empty,
            estados.GetValueOrDefault(estadoAnteriorId ?? string.Empty, estadoAnteriorId ?? "Sin estado"),
            estadoNuevoId ?? string.Empty,
            estados.GetValueOrDefault(estadoNuevoId ?? string.Empty, estadoNuevoId ?? "Sin estado"),
            string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(),
            detalleId,
            string.IsNullOrWhiteSpace(producto) ? null : producto.Trim()));

        pedido.FlujoJson = JsonSerializer.Serialize(eventos, JsonOptions);
    }

    public IReadOnlyList<PedidoFlujoEventoDto> Obtener(VentaImpresionCab pedido)
    {
        if (string.IsNullOrWhiteSpace(pedido.FlujoJson))
        {
            return Array.Empty<PedidoFlujoEventoDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<PedidoFlujoEventoDto>>(pedido.FlujoJson, JsonOptions)
                ?? new List<PedidoFlujoEventoDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<PedidoFlujoEventoDto>();
        }
    }

    private int? CurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var value = user?.FindFirstValue("usuarioId") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    private async Task<string> NombreUsuarioAsync(int? usuarioId, CancellationToken cancellationToken)
    {
        if (!usuarioId.HasValue)
        {
            return "Sistema";
        }

        var usuario = await _context.Usuarios
            .AsNoTracking()
            .Include(x => x.Persona)
            .FirstOrDefaultAsync(x => x.Id == usuarioId.Value, cancellationToken);

        if (usuario is null)
        {
            return $"Usuario {usuarioId.Value}";
        }

        var nombrePersona = string.Join(" ", new[]
        {
            usuario.Persona?.PrimerNombre,
            usuario.Persona?.SegundoNombre,
            usuario.Persona?.PrimerApellido,
            usuario.Persona?.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(nombrePersona)
            ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}"
            : nombrePersona;
    }

    private async Task<Dictionary<string, string>> NombresEstadosAsync(
        string? estadoAnteriorId,
        string? estadoNuevoId,
        CancellationToken cancellationToken)
    {
        var ids = new[] { estadoAnteriorId, estadoNuevoId }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre ?? x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }
}