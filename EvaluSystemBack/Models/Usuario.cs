namespace EvaluSystemBack.Models;

public class Usuario
{
    public int Id { get; set; }
    public string? NombreUsuario { get; set; }
    public string? PassHash { get; set; }
    public int? PersonaId { get; set; }
    public bool? Estado { get; set; }
    public DateOnly FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateOnly FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Persona? Persona { get; set; }
}
