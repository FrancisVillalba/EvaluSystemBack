using System.ComponentModel.DataAnnotations;

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
    [Range(1, int.MaxValue)] int VentaImpresionId,
    [Required, StringLength(1)] string FormaPagoId,
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal Monto,
    string? RutaComprobante,
    [StringLength(255)] string? NombreComprobante);
