namespace EvaluSystemBack.Models;

public class EstadoVenta
{
    public string Id { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string? Estado { get; set; }
    public int? NumeroFlujo { get; set; }

    public ICollection<VentaImpresionCab> Ventas { get; set; } = new List<VentaImpresionCab>();
}
