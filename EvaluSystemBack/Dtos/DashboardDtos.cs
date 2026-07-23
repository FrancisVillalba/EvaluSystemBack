namespace EvaluSystemBack.Dtos;

public record DashboardSummaryDto(
    int TotalPedidos,
    int PedidosCargados,
    int PedidosImpresos,
    int PedidosPendientesImpresion,
    int PedidosEntregados,
    decimal TotalPedidosMensuales,
    DashboardGoalDto MetaMensualTotal,
    IEnumerable<DashboardMachineDto> PedidosPorMaquina,
    IEnumerable<DashboardMachineDto> PedidosMensualesPorMaquina,
    IEnumerable<DashboardGoalDto> MetasMensualesPorMaquina,
    IEnumerable<DashboardMoneyDto> PendientesPago,
    IEnumerable<DashboardSellerDto> MejoresVendedores);

public record DashboardMachineDto(string Nombre, decimal Cantidad);

public record DashboardGoalDto(string Nombre, decimal Cantidad, decimal Meta, decimal Faltante, decimal Porcentaje, bool Cumplido);

public record DashboardMoneyDto(string Nombre, decimal Monto);

public record DashboardSellerDto(string Nombre, decimal Monto);

public record DashboardPedidoDto(
    int Id,
    DateTime Fecha,
    string Cliente,
    string Vendedor,
    string Estado,
    string FormaPago,
    string MetodoEntrega,
    decimal TotalVenta,
    decimal MontoPagado,
    decimal SaldoPendiente);
