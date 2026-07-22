using EvaluSystemBack.Models;

namespace EvaluSystemBack.Services.Interfaces;

public interface IEstadoVentaFlujoService
{
    Task<EstadoVenta?> ObtenerPorIdAsync(string estadoId, CancellationToken cancellationToken);
    Task<EstadoVenta?> ObtenerAnteriorAsync(string estadoId, CancellationToken cancellationToken);
    Task<EstadoVenta?> ObtenerSiguienteAsync(string estadoId, CancellationToken cancellationToken);
    Task<EstadoVenta?> ObtenerSiguienteAsync(EstadoVenta? estadoActual, CancellationToken cancellationToken);
}