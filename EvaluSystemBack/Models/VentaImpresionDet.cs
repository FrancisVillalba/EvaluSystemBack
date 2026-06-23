namespace EvaluSystemBack.Models;

public class VentaImpresionDet
{
    public int Id { get; set; }
    public int CabId { get; set; }
    public int ProductoId { get; set; }
    public int TipoMaquinaId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal? PrecioExtra { get; set; }
    public decimal? PrecioTotal { get; private set; }
    public string? ArchivoDisenio { get; set; }
    public string? ArchivoDisenioNombre { get; set; }
    public string? Observacion { get; set; }
    public string EstadoItem { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public bool? CheckImpresion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public VentaImpresionCab? Cabecera { get; set; }
    public Producto? Producto { get; set; }
    public TipoMaquina? TipoMaquina { get; set; }
}
