namespace EvaluSystemBack.Models;

public class MetodoEnvio
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Estado { get; set; }

    public ICollection<VentaImpresionCab> Ventas { get; set; } = new List<VentaImpresionCab>();
}
