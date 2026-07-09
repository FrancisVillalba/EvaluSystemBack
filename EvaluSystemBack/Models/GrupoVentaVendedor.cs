namespace EvaluSystemBack.Models;

public class GrupoVentaVendedor
{
    public int Id { get; set; }
    public int GrupoVentaId { get; set; }
    public int VendedorUsuarioId { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public GrupoVenta GrupoVenta { get; set; } = null!;
    public Usuario VendedorUsuario { get; set; } = null!;
}
