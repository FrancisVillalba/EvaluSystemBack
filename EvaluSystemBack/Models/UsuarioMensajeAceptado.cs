namespace EvaluSystemBack.Models;

public class UsuarioMensajeAceptado
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Clave { get; set; } = string.Empty;
    public DateTime FechaAceptado { get; set; }

    public Usuario? Usuario { get; set; }
}
