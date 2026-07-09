namespace EvaluSystemBack.Models;

public class GrupoVenta
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int TeamLeaderUsuarioId { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Usuario TeamLeaderUsuario { get; set; } = null!;
    public ICollection<GrupoVentaVendedor> Vendedores { get; set; } = new List<GrupoVentaVendedor>();
}
