namespace EvaluSystemBack.Dtos;

public record ReporteComisionesDto(
    DateTime FechaDesde,
    DateTime FechaHasta,
    IEnumerable<ReporteComisionVendedorDto> Vendedores);

public record ReporteComisionVendedorDto(
    int VendedorId,
    string Vendedor,
    int CantidadPedidos,
    decimal TotalVenta,
    decimal TotalComision,
    IEnumerable<ReporteComisionDetalleDto> Detalles);

public record ReporteComisionDetalleDto(
    int PedidoId,
    DateTime Fecha,
    string Cliente,
    string Producto,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal PrecioExtra,
    decimal TotalDetalle,
    decimal ComisionUnitario,
    decimal ComisionTotal);

public record LotePagoDto(
    int Id,
    string TipoPago,
    DateTime FechaGeneracion,
    string UsuarioGenero,
    DateTime FechaDesde,
    DateTime FechaHasta,
    DateTime FechaPago,
    string? Vendedor,
    decimal MontoTotal,
    int CantidadPersonas,
    string NombreArchivo,
    string Estado);

public record LotePagoEstadoRequest(string Estado);

public record ReporteEnviosDto(
    DateTime FechaDesde,
    DateTime FechaHasta,
    IEnumerable<ReporteEnvioResumenDto> Resumen,
    IEnumerable<ReporteEnvioDetalleDto> Detalles);

public record ReporteEnvioResumenDto(
    int? UsuarioEntregaId,
    string UsuarioEntrega,
    int CantidadPedidos,
    int CantidadTransportadora,
    decimal TotalPedidos);

public record ReporteEnvioDetalleDto(
    int PedidoId,
    DateTime Fecha,
    string Cliente,
    string MetodoEntregaId,
    string MetodoEntrega,
    string Estado,
    string? UsuarioEntrega,
    string? Ciudad,
    decimal TotalPedido);
