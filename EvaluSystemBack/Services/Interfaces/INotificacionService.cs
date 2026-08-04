namespace EvaluSystemBack.Services.Interfaces;

public interface INotificacionService
{
    Task CrearParaUsuarioAsync(int usuarioId, string tipo, string titulo, string mensaje, int pedidoId, int? detalleId, string? producto, string? comentario, CancellationToken cancellationToken);
    Task AsegurarTablaAsync(CancellationToken cancellationToken);
}