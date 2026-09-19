namespace EvaluSystemBack.Models;

public class ProductoComision
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public int PerfilId { get; set; }
    public decimal Porcentaje { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int UsuCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int UsuModificacion { get; set; }

    public Producto Producto { get; set; } = null!;
    public Perfil Perfil { get; set; } = null!;
}
