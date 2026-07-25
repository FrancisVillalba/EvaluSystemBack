namespace EvaluSystemBack.Dtos;

public record NotificacionDto(long Id, string Tipo, string Titulo, string Mensaje, int PedidoId, int? DetalleId, string? Producto, string? Comentario, bool Leida, DateTime FechaCreacion, DateTime? FechaLectura);
public record NotificacionesResumenDto(int NoLeidas, IEnumerable<NotificacionDto> Items);