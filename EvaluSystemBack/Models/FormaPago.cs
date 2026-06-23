namespace EvaluSystemBack.Models;

public class FormaPago
{
    public string Id { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public bool? Estado { get; set; }

    public ICollection<VentaImpresionCab> Ventas { get; set; } = new List<VentaImpresionCab>();
}
