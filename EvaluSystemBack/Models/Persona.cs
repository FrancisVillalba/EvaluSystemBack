namespace EvaluSystemBack.Models;

public class Persona
{
    public int Id { get; set; }
    public int? PerfilId { get; set; }
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public DateTime? FechaCumpleanios { get; set; }
    public bool? Estado { get; set; }
    public DateOnly FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateOnly FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }
    public string? TipoDocumentoId { get; set; }
    public string? Documento { get; set; }

    public Perfil? Perfil { get; set; }
    public TipoDocumento? TipoDocumento { get; set; }
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
