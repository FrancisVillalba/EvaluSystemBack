namespace EvaluSystemBack.Models;

public class TipoMaquina
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
    public ICollection<VentaImpresionDet> VentasDetalle { get; set; } = new List<VentaImpresionDet>();
}
