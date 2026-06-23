namespace EvaluSystemBack.Models;

public class EstadoPago : SimpleStringCatalog
{
    public ICollection<VentaImpresionCab> Ventas { get; set; } = new List<VentaImpresionCab>();
}
