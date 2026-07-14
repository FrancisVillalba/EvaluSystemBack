namespace EvaluSystemBack.Models;

public class Formulario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Ruta { get; set; }
    public string? Icono { get; set; }
    public int Orden { get; set; }
    public bool Estado { get; set; }
    public int? FormularioPadreId { get; set; }

    public Formulario? FormularioPadre { get; set; }
    public ICollection<Formulario> FormulariosHijos { get; set; } = new List<Formulario>();
    public ICollection<PerfilFormularioPermiso> PerfilPermisos { get; set; } = new List<PerfilFormularioPermiso>();
}
