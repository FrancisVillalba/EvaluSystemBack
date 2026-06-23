namespace EvaluSystemBack.Models;

public class PerfilFormularioPermiso
{
    public int Id { get; set; }
    public int PerfilId { get; set; }
    public int FormularioId { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public Perfil? Perfil { get; set; }
    public Formulario? Formulario { get; set; }
}
