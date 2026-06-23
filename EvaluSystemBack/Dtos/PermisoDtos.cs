using System.ComponentModel.DataAnnotations;

namespace EvaluSystemBack.Dtos;

public record FormularioDto(
    int Id,
    string Nombre,
    string? Descripcion,
    string? Ruta,
    string? Icono,
    int Orden,
    bool Estado);

public record FormularioRequest(
    [Required] string Nombre,
    string? Descripcion,
    string? Ruta,
    string? Icono,
    int Orden,
    bool Estado);

public record PerfilFormularioPermisoDto(
    int Id,
    int PerfilId,
    string? Perfil,
    int FormularioId,
    string Formulario,
    string? Descripcion,
    string? Ruta,
    string? Icono,
    int Orden,
    bool PuedeVer,
    bool PuedeCrear,
    bool PuedeEditar,
    bool PuedeEliminar);

public record PerfilFormularioPermisoRequest(
    int PerfilId,
    int FormularioId,
    bool PuedeVer,
    bool PuedeCrear,
    bool PuedeEditar,
    bool PuedeEliminar);
