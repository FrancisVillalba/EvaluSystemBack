namespace EvaluSystemBack.Models;

public class PagoVentaImpresion
{
    public int Id { get; set; }
    public int VentaImpresionId { get; set; }
    public DateTime FechaHora { get; set; }
    public int UsuarioId { get; set; }
    public string FormaPagoId { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string? RutaComprobante { get; set; }
    public string? NombreComprobante { get; set; }

    public VentaImpresionCab? Venta { get; set; }
    public Usuario? Usuario { get; set; }
}
