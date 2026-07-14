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
        var perfilIds = await PerfilIdsPorUsuarioAsync(usuarioId);

        if (perfilIds.Count == 0)
        {
            return Enumerable.Empty<PerfilFormularioPermisoDto>();
        }

        if (await UsuarioEsAdministradorAsync(usuarioId))
        {
            var formulariosAdmin = await _context.Formularios
                .AsNoTracking()
                .Where(x => x.Estado)
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.Nombre)
                .ToListAsync();

            return formulariosAdmin.Select(form => new PerfilFormularioPermisoDto(
                0,
                0,
                "Administrador",
                form.Id,
                form.Nombre,
                form.Descripcion,
                form.Ruta,
                form.Icono,
                form.Orden,
                true,
                true,
                true,
                true));
        }

        var permisos = await _context.PerfilFormularioPermisos
            .Include(x => x.Perfil)
            .Include(x => x.Formulario)
            .AsNoTracking()
            .Where(x => perfilIds.Contains(x.PerfilId) && x.Formulario != null && x.Formulario.Estado && x.PuedeVer)
            .GroupBy(x => x.FormularioId)
            .Select(group => new
            {
                FormularioId = group.Key,
                PuedeVer = group.Any(x => x.PuedeVer),
                PuedeCrear = group.Any(x => x.PuedeCrear),
                PuedeEditar = group.Any(x => x.PuedeEditar),
                PuedeEliminar = group.Any(x => x.PuedeEliminar)
            })
            .ToListAsync();

        var formularios = await _context.Formularios
            .AsNoTracking()
            .Where(x => permisos.Select(p => p.FormularioId).Contains(x.Id))
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        return formularios.Select(form =>
        {
            var permiso = permisos.First(x => x.FormularioId == form.Id);
            return new PerfilFormularioPermisoDto(
                0,
                0,
                "Perfiles del usuario",
                form.Id,
                form.Nombre,
                form.Descripcion,
                form.Ruta,
                form.Icono,
                form.Orden,
                permiso.PuedeVer,
                permiso.PuedeCrear,
                permiso.PuedeEditar,
                permiso.PuedeEliminar);
        });
    }

    private async Task<List<int>> PerfilIdsPorUsuarioAsync(int usuarioId)
    {
        var perfilIds = await _context.UsuarioPerfiles
            .Where(x => x.UsuarioId == usuarioId && x.Estado)
            .Select(x => x.PerfilId)
            .Distinct()
            .ToListAsync();

        return perfilIds;
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
        var perfilIds = await PerfilIdsPorUsuarioAsync(usuarioId);
        if (perfilIds.Count == 0)
        {
            return false;
        }

        if (await UsuarioEsAdministradorAsync(usuarioId))
        {
            return true;
        }

        var permisos = await _context.PerfilFormularioPermisos
            .Include(x => x.Formulario)
            .Where(x =>
                x.Formulario != null &&
                x.Formulario.Nombre == formulario &&
                x.Formulario.Estado &&
                perfilIds.Contains(x.PerfilId))
            .Select(x => new
            {
                x.PuedeVer,
                x.PuedeCrear,
                x.PuedeEditar,
                x.PuedeEliminar
            })
            .ToListAsync();

        return accion.ToLowerInvariant() switch
        {
            "ver" => permisos.Any(x => x.PuedeVer),
            "crear" => permisos.Any(x => x.PuedeCrear),
            "editar" => permisos.Any(x => x.PuedeEditar),
            "eliminar" => permisos.Any(x => x.PuedeEliminar),
            _ => false
        };
    }

    private async Task<bool> UsuarioEsAdministradorAsync(int usuarioId)
    {
        var adminEnPerfiles = await _context.UsuarioPerfiles
            .Include(x => x.Perfil)
            .AnyAsync(x => x.UsuarioId == usuarioId &&
                x.Estado &&
                x.Perfil != null &&
                x.Perfil.Estado &&
                x.Perfil.Nombre == "Administrador");

        return adminEnPerfiles;
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
