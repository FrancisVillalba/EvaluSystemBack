namespace EvaluSystemBack.Models;

public class RutaDeliveryDetalle
{
    public int Id { get; set; }
    public int RutaDeliveryId { get; set; }
    public int VentaId { get; set; }
    public DateTime FechaAgregado { get; set; }

    public RutaDelivery? RutaDelivery { get; set; }
    public VentaImpresionCab? Venta { get; set; }
}
