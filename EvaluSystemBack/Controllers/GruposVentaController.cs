using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GruposVentaController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public GruposVentaController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GrupoVentaDto>>> GetAll()
    {
        var items = await Query().OrderBy(x => x.Nombre).ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GrupoVentaDto>> GetById(int id)
    {
        var item = await Query().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpGet("mi-equipo")]
    public async Task<ActionResult<GrupoVentaEquipoDto>> GetMiEquipo([FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null)
    {
        var usuarioId = CurrentUserId();
        if (!usuarioId.HasValue)
        {
            return Unauthorized();
        }

        var from = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var to = (dateTo ?? DateTime.Today).Date;
        var toExclusive = to.AddDays(1);

        var isAdmin = await UserHasProfileAsync(usuarioId.Value, "Administrador");
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
            return Ok(new GrupoVentaEquipoDto(from, to, [], []));
        }

        var vendedoresActivos = grupos
            .SelectMany(x => x.Vendedores)
            .Where(x => x.Estado)
            .GroupBy(x => x.VendedorUsuarioId)
            .Select(x => x.First())
            .ToList();
        var vendedorIds = vendedoresActivos.Select(x => x.VendedorUsuarioId).ToHashSet();

        if (vendedorIds.Count == 0)
        {
            return Ok(new GrupoVentaEquipoDto(from, to, [], []));
        }

        var vendedores = vendedoresActivos.ToDictionary(
            x => x.VendedorUsuarioId,
            x => x.VendedorUsuario is null ? $"Usuario {x.VendedorUsuarioId}" : NombreUsuario(x.VendedorUsuario));
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

        var resumen = ventas
            .GroupBy(x => x.VendedorId)
            .Select(x => new GrupoVentaResumenVendedorDto(
                x.Key,
                vendedores.GetValueOrDefault(x.Key, $"Usuario {x.Key}"),
                x.Count(),
                x.Sum(item => item.TotalVenta),
                x.Sum(item => item.Detalles.Sum(detail => detail.Cantidad))))
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
                x.Detalles.Sum(detail => detail.Cantidad)))
            .ToList();

        return Ok(new GrupoVentaEquipoDto(from, to, resumen, detalle));
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

        if (hasProfile)
        {
            return true;
        }

        return await _context.Usuarios
            .Include(x => x.Persona)
            .ThenInclude(x => x!.Perfil)
            .AnyAsync(x => x.Id == usuarioId &&
                x.Persona != null &&
                x.Persona.Perfil != null &&
                x.Persona.Perfil.Estado &&
                x.Persona.Perfil.Nombre == profileName);
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
}
