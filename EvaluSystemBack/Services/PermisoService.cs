using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class PermisoService : IPermisoService
{
    private readonly EvaluSystemDbContext _context;

    public PermisoService(EvaluSystemDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<PerfilFormularioPermisoDto>> ObtenerPermisosPorUsuarioAsync(int usuarioId)
    {
        var perfilId = await _context.Usuarios
            .Where(x => x.Id == usuarioId)
            .Select(x => x.Persona != null ? x.Persona.PerfilId : null)
            .FirstOrDefaultAsync();

        return perfilId.HasValue
            ? await ObtenerPermisosPorPerfilAsync(perfilId.Value)
            : Enumerable.Empty<PerfilFormularioPermisoDto>();
    }

    public async Task<IEnumerable<PerfilFormularioPermisoDto>> ObtenerPermisosPorPerfilAsync(int perfilId)
    {
        var permisos = await _context.PerfilFormularioPermisos
            .Include(x => x.Perfil)
            .Include(x => x.Formulario)
            .AsNoTracking()
            .Where(x => x.PerfilId == perfilId && x.Formulario != null && x.Formulario.Estado && x.PuedeVer)
            .OrderBy(x => x.Formulario!.Orden)
            .ThenBy(x => x.Formulario!.Nombre)
            .ToListAsync();

        return permisos.Select(ToDto);
    }

    public async Task<bool> UsuarioTienePermisoAsync(int usuarioId, string formulario, string accion)
    {
        var permiso = await _context.PerfilFormularioPermisos
            .Include(x => x.Formulario)
            .Where(x =>
                x.Formulario != null &&
                x.Formulario.Nombre == formulario &&
                x.Formulario.Estado &&
                x.Perfil != null &&
                x.Perfil.Personas.Any(p => p.Usuarios.Any(u => u.Id == usuarioId)))
            .Select(x => new
            {
                x.PuedeVer,
                x.PuedeCrear,
                x.PuedeEditar,
                x.PuedeEliminar
            })
            .FirstOrDefaultAsync();

        return accion.ToLowerInvariant() switch
        {
            "ver" => permiso?.PuedeVer == true,
            "crear" => permiso?.PuedeCrear == true,
            "editar" => permiso?.PuedeEditar == true,
            "eliminar" => permiso?.PuedeEliminar == true,
            _ => false
        };
    }

    public async Task<IEnumerable<FormularioDto>> GetFormulariosAsync()
    {
        var formularios = await _context.Formularios
            .AsNoTracking()
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        return formularios.Select(ToDto);
    }

    public async Task<FormularioDto> SaveFormularioAsync(FormularioRequest request)
    {
        var formulario = await _context.Formularios.FirstOrDefaultAsync(x => x.Nombre == request.Nombre);
        if (formulario is null)
        {
            formulario = new Formulario();
            _context.Formularios.Add(formulario);
        }

        formulario.Nombre = request.Nombre;
        formulario.Descripcion = request.Descripcion;
        formulario.Ruta = request.Ruta;
        formulario.Icono = request.Icono;
        formulario.Orden = request.Orden;
        formulario.Estado = request.Estado;

        await _context.SaveChangesAsync();
        return ToDto(formulario);
    }

    public async Task<PerfilFormularioPermisoDto> SavePermisoAsync(PerfilFormularioPermisoRequest request)
    {
        var permiso = await _context.PerfilFormularioPermisos
            .Include(x => x.Perfil)
            .Include(x => x.Formulario)
            .FirstOrDefaultAsync(x => x.PerfilId == request.PerfilId && x.FormularioId == request.FormularioId);

        if (permiso is null)
        {
            permiso = new PerfilFormularioPermiso
            {
                PerfilId = request.PerfilId,
                FormularioId = request.FormularioId
            };
            _context.PerfilFormularioPermisos.Add(permiso);
        }

        permiso.PuedeVer = request.PuedeVer;
        permiso.PuedeCrear = request.PuedeCrear;
        permiso.PuedeEditar = request.PuedeEditar;
        permiso.PuedeEliminar = request.PuedeEliminar;

        await _context.SaveChangesAsync();

        await _context.Entry(permiso).Reference(x => x.Perfil).LoadAsync();
        await _context.Entry(permiso).Reference(x => x.Formulario).LoadAsync();

        return ToDto(permiso);
    }

    private static FormularioDto ToDto(Formulario entity)
    {
        return new FormularioDto(entity.Id, entity.Nombre, entity.Descripcion, entity.Ruta, entity.Icono, entity.Orden, entity.Estado);
    }

    private static PerfilFormularioPermisoDto ToDto(PerfilFormularioPermiso entity)
    {
        return new PerfilFormularioPermisoDto(
            entity.Id,
            entity.PerfilId,
            entity.Perfil?.Nombre,
            entity.FormularioId,
            entity.Formulario?.Nombre ?? string.Empty,
            entity.Formulario?.Descripcion,
            entity.Formulario?.Ruta,
            entity.Formulario?.Icono,
            entity.Formulario?.Orden ?? 0,
            entity.PuedeVer,
            entity.PuedeCrear,
            entity.PuedeEditar,
            entity.PuedeEliminar);
    }
}
