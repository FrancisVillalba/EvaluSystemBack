using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;

namespace EvaluSystemBack.Services.Interfaces;

public interface IPedidoFlujoService
{
    Task RegistrarAsync(
        VentaImpresionCab pedido,
        string accion,
        string? estadoAnteriorId,
        string? estadoNuevoId,
        string? comentario = null,
        int? detalleId = null,
        int? usuarioId = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<PedidoFlujoEventoDto> Obtener(VentaImpresionCab pedido);
}