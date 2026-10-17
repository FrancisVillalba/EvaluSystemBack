namespace EvaluSystemBack.Dtos;

public record ReporteVentaProductoDto(int DetalleId, int PedidoId, DateTime Fecha, string Cliente,
    string? Documento, string? Telefono, string? Email, string Vendedor, string Producto,
    decimal Cantidad, decimal PrecioUnitario, decimal Extra, decimal MontoVenta,
    decimal PorcentajeComision, decimal Comision, bool GeneraComision, string EstadoVenta, string EstadoPago);
