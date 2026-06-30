namespace EvaluSystemBack.Models;

public class UsuarioPerfil
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public int PerfilId { get; set; }
    public bool Estado { get; set; } = true;
    public DateTime FechaCreacion { get; set; }

    public Usuario? Usuario { get; set; }
    public Perfil? Perfil { get; set; }
}
