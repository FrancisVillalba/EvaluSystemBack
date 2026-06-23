using System.ComponentModel.DataAnnotations;

namespace EvaluSystemBack.Dtos;

public record LoginRequest([Required] string Usuario, [Required] string Pass);

public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    int UsuarioId,
    string Usuario,
    int? PersonaId,
    string? Persona,
    IEnumerable<PerfilFormularioPermisoDto> Permisos);
