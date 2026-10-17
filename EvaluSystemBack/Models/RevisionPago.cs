namespace EvaluSystemBack.Models;

public class RevisionPago
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Comentario { get; set; }
    public int UsuarioId { get; set; }
    public DateTime Fecha { get; set; }
}
