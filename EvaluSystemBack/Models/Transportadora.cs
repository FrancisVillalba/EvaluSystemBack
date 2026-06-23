namespace EvaluSystemBack.Models;

public class Transportadora
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Observacion { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public ICollection<ClienteDatosEnvio> ClienteDatosEnvios { get; set; } = new List<ClienteDatosEnvio>();
}
