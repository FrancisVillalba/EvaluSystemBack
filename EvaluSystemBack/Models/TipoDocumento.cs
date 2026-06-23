namespace EvaluSystemBack.Models;

public class TipoDocumento : SimpleStringCatalog
{
    public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    public ICollection<Persona> Personas { get; set; } = new List<Persona>();
}
