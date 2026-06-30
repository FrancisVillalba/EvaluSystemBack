using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsuariosController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    private readonly IPasswordService _passwordService;

    public UsuariosController(EvaluSystemDbContext context, IPasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UsuarioDto>>> GetAll()
    {
        var items = await _context.Usuarios
            .Include(x => x.Persona)
            .ThenInclude(x => x!.Perfil)
            .Include(x => x.Perfiles)
            .ThenInclude(x => x.Perfil)
            .AsNoTracking()
            .ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UsuarioDto>> GetById(int id)
    {
        var item = await _context.Usuarios
            .Include(x => x.Persona)
            .ThenInclude(x => x!.Perfil)
            .Include(x => x.Perfiles)
            .ThenInclude(x => x.Perfil)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<UsuarioDto>> Create(UsuarioRequest request)
    {
        var item = request.ToEntity();
        if (!string.IsNullOrWhiteSpace(request.Pass))
        {
            item.PassHash = _passwordService.HashPassword(request.Pass);
        }

        _context.Usuarios.Add(item);
        await _context.SaveChangesAsync();
        await SyncPerfilesAsync(item.Id, request.PerfilIds);
        var created = await QueryUsuario().FirstAsync(x => x.Id == item.Id);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, created.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UsuarioRequest request)
    {
        var item = await _context.Usuarios
            .Include(x => x.Perfiles)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return NotFound();
        }

        request.ToEntity(item);
        if (!string.IsNullOrWhiteSpace(request.Pass))
        {
            item.PassHash = _passwordService.HashPassword(request.Pass);
        }

        await SyncPerfilesAsync(item.Id, request.PerfilIds);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Usuarios.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.Usuarios.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Usuario> QueryUsuario()
    {
        return _context.Usuarios
            .Include(x => x.Persona)
            .ThenInclude(x => x!.Perfil)
            .Include(x => x.Perfiles)
            .ThenInclude(x => x.Perfil)
            .AsNoTracking();
    }

    private async Task SyncPerfilesAsync(int usuarioId, IEnumerable<int>? perfilIds)
    {
        var ids = (perfilIds ?? Enumerable.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            var perfilPersona = await _context.Usuarios
                .Where(x => x.Id == usuarioId)
                .Select(x => x.Persona != null ? x.Persona.PerfilId : null)
                .FirstOrDefaultAsync();

            if (perfilPersona.HasValue)
            {
                ids.Add(perfilPersona.Value);
            }
        }

        var actuales = await _context.UsuarioPerfiles
            .Where(x => x.UsuarioId == usuarioId)
            .ToListAsync();

        foreach (var actual in actuales)
        {
            actual.Estado = ids.Contains(actual.PerfilId);
        }

        foreach (var perfilId in ids.Where(id => actuales.All(x => x.PerfilId != id)))
        {
            _context.UsuarioPerfiles.Add(new UsuarioPerfil
            {
                UsuarioId = usuarioId,
                PerfilId = perfilId,
                Estado = true,
                FechaCreacion = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
    }
}
