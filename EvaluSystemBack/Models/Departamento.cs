namespace EvaluSystemBack.Models;

public class Departamento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Estado { get; set; }

    public ICollection<Ciudad> Ciudades { get; set; } = new List<Ciudad>();
    public ICollection<ClienteDatosEnvio> ClienteDatosEnvios { get; set; } = new List<ClienteDatosEnvio>();
}
