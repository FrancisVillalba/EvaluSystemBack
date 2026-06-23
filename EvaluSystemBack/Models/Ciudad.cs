namespace EvaluSystemBack.Models;

public class Ciudad
{
    public int Id { get; set; }
    public int DepartamentoId { get; set; }
    public int CodigoDistrito { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Estado { get; set; }

    public Departamento? Departamento { get; set; }
    public ICollection<ClienteDatosEnvio> ClienteDatosEnvios { get; set; } = new List<ClienteDatosEnvio>();
}
