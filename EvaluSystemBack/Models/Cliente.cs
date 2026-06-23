namespace EvaluSystemBack.Models;

public class Cliente
{
    public int Id { get; set; }
    public string? Nombre { get; set; }
    public string? Documento { get; set; }
    public string TipoDocumentoId { get; set; } = string.Empty;
    public string TipoClienteId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? NroTelefono { get; set; }
    public string? Direccion { get; set; }
    public bool? Estado { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuModificacion { get; set; }
    public DateTime FechaModificacion { get; set; }

    public TipoCliente? TipoCliente { get; set; }
    public TipoDocumento? TipoDocumento { get; set; }
    public ClienteDatosEnvio? DatosEnvio { get; set; }
    public ICollection<VentaImpresionCab> Ventas { get; set; } = new List<VentaImpresionCab>();
}
