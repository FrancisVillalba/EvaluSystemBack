namespace EvaluSystemBack.Models;

public class Perfil
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public ICollection<PerfilFormularioPermiso> FormularioPermisos { get; set; } = new List<PerfilFormularioPermiso>();
    public ICollection<UsuarioPerfil> Usuarios { get; set; } = new List<UsuarioPerfil>();
    public ICollection<ProductoComision> ProductoComisiones { get; set; } = new List<ProductoComision>();
}
