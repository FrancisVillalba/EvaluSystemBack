namespace EvaluSystemBack.Models;

public class Notificacion
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int PedidoId { get; set; }
    public int? DetalleId { get; set; }
    public string? Producto { get; set; }
    public string? Comentario { get; set; }
    public bool Leida { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaLectura { get; set; }
    public Usuario? Usuario { get; set; }
}