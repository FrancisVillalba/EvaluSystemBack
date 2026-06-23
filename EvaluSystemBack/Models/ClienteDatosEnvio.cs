namespace EvaluSystemBack.Models;

public class ClienteDatosEnvio
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int TransportadoraId { get; set; }
    public string NombreReceptor { get; set; } = string.Empty;
    public string DocumentoReceptor { get; set; } = string.Empty;
    public string TelefonoReceptor { get; set; } = string.Empty;
    public int DepartamentoId { get; set; }
    public int CiudadId { get; set; }
    public string Direccion { get; set; } = string.Empty;
    public string? Observacion { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Cliente? Cliente { get; set; }
    public Transportadora? Transportadora { get; set; }
    public Departamento? Departamento { get; set; }
    public Ciudad? Ciudad { get; set; }
}
