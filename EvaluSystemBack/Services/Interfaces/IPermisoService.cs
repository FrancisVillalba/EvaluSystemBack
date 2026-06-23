using EvaluSystemBack.Dtos;

namespace EvaluSystemBack.Services.Interfaces;

public interface IPermisoService
{
    Task<IEnumerable<PerfilFormularioPermisoDto>> ObtenerPermisosPorUsuarioAsync(int usuarioId);
    Task<IEnumerable<PerfilFormularioPermisoDto>> ObtenerPermisosPorPerfilAsync(int perfilId);
    Task<bool> UsuarioTienePermisoAsync(int usuarioId, string formulario, string accion);
    Task<IEnumerable<FormularioDto>> GetFormulariosAsync();
    Task<FormularioDto> SaveFormularioAsync(FormularioRequest request);
    Task<PerfilFormularioPermisoDto> SavePermisoAsync(PerfilFormularioPermisoRequest request);
}
