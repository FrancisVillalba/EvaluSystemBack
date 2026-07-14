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
    decimal ComisionTotal,
    string? VendedorOrigen = null);

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

public record ReporteResumenGerencialDto(
    DateTime FechaDesde,
    DateTime FechaHasta,
    decimal TotalVendido,
    int CantidadPedidos,
    decimal PromedioPedido,
    decimal TotalVendidoComisionPagada,
    decimal TotalComisionPagada,
    IEnumerable<ReporteResumenMaquinaDto> VentasPorMaquina,
    IEnumerable<ReporteResumenPerfilComisionDto> ComisionesPorPerfil);

public record ReporteResumenMaquinaDto(
    string Maquina,
    int CantidadPedidos,
    decimal Cantidad,
    decimal TotalVenta);

public record ReporteResumenPerfilComisionDto(
    string Perfil,
    int CantidadPedidos,
    decimal TotalVendido);

public record ReporteResumenEstadoDto(
    string Estado,
    int NumeroFlujo,
    int CantidadPedidos,
    decimal TotalVenta);

public record ReporteResumenVendedorDto(
    int VendedorId,
    string Vendedor,
    int CantidadPedidos,
    decimal TotalVenta,
    decimal TotalComision);

public record ReporteResumenDeudaDto(
    int PedidoId,
    DateTime Fecha,
    string Cliente,
    string Vendedor,
    decimal TotalVenta,
    decimal MontoPagado,
    decimal Saldo,
    int DiasAtraso);

public record ReporteResumenEntregaDto(
    int? UsuarioEntregaId,
    string UsuarioEntrega,
    int PedidosTomados,
    int PedidosEntregados,
    decimal TotalMovido);
