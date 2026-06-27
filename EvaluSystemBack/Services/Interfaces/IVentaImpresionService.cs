using EvaluSystemBack.Dtos;

namespace EvaluSystemBack.Services.Interfaces;

public interface IVentaImpresionService
{
    Task<VentaImpresionCabDto> CrearVentaCompletaAsync(VentaImpresionCompletaRequest request);
    Task<VentaImpresionCabDto?> ActualizarVentaCompletaAsync(int id, VentaImpresionCompletaUpdateRequest request);
    Task<VentaImpresionCabDto?> ActualizarCabeceraAsync(int id, VentaImpresionCabRequest request);
    Task<VentaImpresionCabDto?> MarcarVentaEliminadaAsync(int id, EliminarVentaImpresionRequest request);
    Task<bool> EliminarVentaAsync(int id);
    Task<VentaImpresionDetDto> CrearDetalleAsync(int cabId, VentaImpresionDetalleCreateRequest request);
    Task<VentaImpresionDetDto?> ActualizarDetalleAsync(int cabId, int detalleId, VentaImpresionDetalleCreateRequest request);
    Task<bool> EliminarDetalleAsync(int cabId, int detalleId);
}
