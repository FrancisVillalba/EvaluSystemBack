namespace EvaluSystemBack.Models;

public class LotePago
{
    public int Id { get; set; }
    public string TipoPago { get; set; } = string.Empty;
    public DateTime FechaGeneracion { get; set; }
    public int UsuarioGeneroId { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public DateTime FechaPago { get; set; }
    public int? VendedorId { get; set; }
    public decimal MontoTotal { get; set; }
    public int CantidadPersonas { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string Estado { get; set; } = "Generado";
    public string ContenidoTxt { get; set; } = string.Empty;

    public Usuario? UsuarioGenero { get; set; }
    public Usuario? Vendedor { get; set; }
    public ICollection<LotePagoDetalle> Detalles { get; set; } = new List<LotePagoDetalle>();
}
