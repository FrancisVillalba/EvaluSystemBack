namespace EvaluSystemBack.Dtos;

public record PagoVentaImpresionDto(
    int Id,
    int VentaImpresionId,
    DateTime FechaHora,
    int UsuarioId,
    string FormaPagoId,
    decimal Monto,
    string? RutaComprobante,
    string? NombreComprobante);

public record PagoVentaImpresionRequest(
    int VentaImpresionId,
    string FormaPagoId,
    decimal Monto,
    string? RutaComprobante,
    string? NombreComprobante);
