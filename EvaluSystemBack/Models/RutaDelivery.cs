namespace EvaluSystemBack.Models;

public class RutaDelivery
{
    public int Id { get; set; }
    public string NumeroLote { get; set; } = string.Empty;
    public int UsuarioDeliveryId { get; set; }
    public DateTime FechaGeneracion { get; set; }
    public string Estado { get; set; } = "Generado";
    public string? Observacion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Usuario? UsuarioDelivery { get; set; }
    public ICollection<RutaDeliveryDetalle> Detalles { get; set; } = new List<RutaDeliveryDetalle>();
}
