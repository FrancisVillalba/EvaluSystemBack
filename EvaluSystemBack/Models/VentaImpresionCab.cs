namespace EvaluSystemBack.Models;

public class VentaImpresionCab
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string FormaPagoId { get; set; } = string.Empty;
    public decimal TotalVenta { get; set; }
    public decimal MontoEnvioTransportadora { get; set; }
    public string EstadoVentaId { get; set; } = string.Empty;
    public int VendedorId { get; set; }
    public decimal? MontoPagado { get; set; }
    public string? EstadoPagadoId { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public string? ComprobantePago { get; set; }
    public string? ComprobantePagoNombre { get; set; }
    public string? Observacion { get; set; }
    public string? MetodoEntrega { get; set; }
    public bool Reposicion { get; set; }
    public int? UsuarioEntregaPedidoId { get; set; }
    public DateTime? FechaTomaDelivery { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Cliente? Cliente { get; set; }
    public FormaPago? FormaPago { get; set; }
    public EstadoPago? EstadoPago { get; set; }
    public EstadoVenta? EstadoVenta { get; set; }
    public MetodoEnvio? MetodoEnvio { get; set; }
    public Usuario? UsuarioEntregaPedido { get; set; }
    public ICollection<VentaImpresionDet> Detalles { get; set; } = new List<VentaImpresionDet>();
}
