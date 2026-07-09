namespace EvaluSystemBack.Models;

public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
    public int? MaquinaId { get; set; }
    public bool Estado { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuModificacion { get; set; }
    public DateTime FechaModificacion { get; set; }

    public TipoMaquina? TipoMaquina { get; set; }
    public ICollection<ProductoComision> Comisiones { get; set; } = new List<ProductoComision>();
    public ICollection<VentaImpresionDet> VentasDetalle { get; set; } = new List<VentaImpresionDet>();
}
