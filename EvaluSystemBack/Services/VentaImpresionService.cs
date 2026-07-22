using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class VentaImpresionService : IVentaImpresionService
{
    private const string EstadoVentaCarga = "PC";
    private const string EstadoVentaInicial = "PI";
    private const string EstadoVentaLimiteEditable = "PE";
    private const string EstadoVentaEliminado = "XX";
    private const string EstadoDetalleInicial = "IP";
    private const string EstadoPagoPendiente = "P1";
    private const string EstadoPagoParcial = "P2";
    private const string EstadoPagoPagado = "P3";

    private const string MetodoEntregaDelivery = "DELIVERY";
    private const string MetodoEntregaTransportadora = "TRANSPORTADORA";
    private const string ConfigMontoEnvioTransportadora = "MONTO_ENVIO_TRANSPORTADORA";
    private const int ConfigMontoEnvioTransportadoraNumero = 1;
    private const int MontoEnvioTransportadoraDefault = 10000;

    private readonly EvaluSystemDbContext _context;
    private readonly IConfiguracionService _configuracionService;
    private readonly IEstadoVentaFlujoService _estadoVentaFlujoService;

    public VentaImpresionService(
        EvaluSystemDbContext context,
        IConfiguracionService configuracionService,
        IEstadoVentaFlujoService estadoVentaFlujoService)
    {
        _context = context;
        _configuracionService = configuracionService;
        _estadoVentaFlujoService = estadoVentaFlujoService;
    }

    public async Task<VentaImpresionCabDto> CrearVentaCompletaAsync(VentaImpresionCompletaRequest request)
    {
        var detalles = request.Detalles?.ToList() ?? new List<VentaImpresionDetalleCreateRequest>();
        if (detalles.Count == 0)
        {
            throw new InvalidOperationException("La venta debe tener al menos un detalle.");
        }

        await ValidarDetallesAsync(detalles);

        var metodoEntrega = NormalizeMetodoEntrega(request.MetodoEntrega);
        var totalVenta = await CalcularTotalVentaAsync(detalles, metodoEntrega);
        await ValidarEstadoInicialAsync(request.EstadoVentaId);
        await ValidarCabeceraAsync(request, totalVenta.TotalVenta);
        await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);
        var estadoVentaId = await ResolverEstadoVentaIdAsync(request.EstadoVentaId);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var cabecera = new VentaImpresionCab
        {
            ClienteId = request.ClienteId,
            FormaPagoId = request.FormaPagoId,
            TotalVenta = totalVenta.TotalVenta,
            MontoEnvioTransportadora = totalVenta.MontoEnvioTransportadora,
            EstadoVentaId = estadoVentaId,
            VendedorId = request.VendedorId,
            MontoPagado = request.MontoPagado ?? 0,
            EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId,
            FechaEntrega = request.FechaEntrega,
            ComprobantePago = NormalizarRutaArchivo(request.ComprobantePago),
            ComprobantePagoNombre = request.ComprobantePagoNombre,
            Observacion = request.Observacion,
            MetodoEntrega = metodoEntrega,
            Reposicion = request.Reposicion
        };

        _context.VentasImpresionCab.Add(cabecera);
        await _context.SaveChangesAsync();

        foreach (var detalleRequest in detalles)
        {
            var detalle = new VentaImpresionDet
            {
                CabId = cabecera.Id,
                ProductoId = detalleRequest.ProductoId,
                TipoMaquinaId = detalleRequest.TipoMaquinaId,
                Cantidad = detalleRequest.Cantidad,
                PrecioUnitario = detalleRequest.PrecioUnitario,
                PrecioExtra = detalleRequest.PrecioExtra ?? 0,
                ArchivoDisenio = NormalizarRutaArchivo(detalleRequest.ArchivoDisenio),
                ArchivoDisenioNombre = detalleRequest.ArchivoDisenioNombre,
                Observacion = detalleRequest.Observacion,
                EstadoItem = string.IsNullOrWhiteSpace(detalleRequest.EstadoItem) ? EstadoDetalleInicial : detalleRequest.EstadoItem,
                CheckImpresion = detalleRequest.CheckImpresion ?? false
            };

            _context.VentasImpresionDet.Add(detalle);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var venta = await QueryVentaCompleta()
            .AsNoTracking()
            .FirstAsync(x => x.Id == cabecera.Id);

        return venta.ToDto();
    }

    public async Task<VentaImpresionCabDto?> ActualizarVentaCompletaAsync(int id, VentaImpresionCompletaUpdateRequest request)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (cabecera is null)
        {
            return null;
        }

        var detalles = request.Detalles?.ToList() ?? new List<VentaImpresionDetalleUpdateRequest>();
        if (detalles.Count == 0)
        {
            throw new InvalidOperationException("La venta debe tener al menos un detalle.");
        }

        var detalleIds = detalles
            .Where(x => x.Id.HasValue)
            .Select(x => x.Id!.Value)
            .ToList();

        if (detalleIds.Count != detalleIds.Distinct().Count())
        {
            throw new InvalidOperationException("Hay detalles repetidos en la solicitud.");
        }

        var idsInvalidos = detalleIds
            .Except(cabecera.Detalles.Select(x => x.Id))
            .ToList();

        if (idsInvalidos.Count > 0)
        {
            throw new InvalidOperationException("Uno o mas detalles no pertenecen a la venta.");
        }

        var metodoEntrega = NormalizeMetodoEntrega(request.MetodoEntrega);
        var totalVenta = await CalcularTotalVentaAsync(detalles, cabecera, metodoEntrega);
        if (EsActualizacionSoloPago(cabecera, request, totalVenta.TotalVenta, detalles))
        {
            await ValidarCamposPagoAsync(request.FormaPagoId, request.MontoPagado, request.EstadoPagadoId, cabecera.TotalVenta);
            await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);

            cabecera.FormaPagoId = request.FormaPagoId;
            cabecera.MontoPagado = request.MontoPagado ?? 0;
            cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
            cabecera.ComprobantePago = NormalizarRutaArchivo(request.ComprobantePago);
            cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;

            await _context.SaveChangesAsync();

            var ventaSoloPago = await QueryVentaCompleta()
                .AsNoTracking()
                .FirstAsync(x => x.Id == id);

            return ventaSoloPago.ToDto();
        }

        await ValidarDetallesAsync(detalles);
        await ValidarCabeceraAsync(request, totalVenta.TotalVenta);
        await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);
        await ValidarVentaEditableAsync(cabecera);
        await ValidarTransicionEstadoAsync(cabecera.EstadoVentaId, request.EstadoVentaId);
        await ValidarAdjuntosParaImpresionAsync(cabecera.EstadoVentaId, request.EstadoVentaId, detalles);
        var estadoVentaId = await ResolverEstadoVentaIdAsync(request.EstadoVentaId);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        cabecera.ClienteId = request.ClienteId;
        cabecera.FormaPagoId = request.FormaPagoId;
        cabecera.EstadoVentaId = estadoVentaId;
        cabecera.VendedorId = request.VendedorId;
        cabecera.MontoPagado = request.MontoPagado ?? 0;
        cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
        cabecera.FechaEntrega = request.FechaEntrega;
        cabecera.ComprobantePago = NormalizarRutaArchivo(request.ComprobantePago);
        cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;
        cabecera.Observacion = request.Observacion;
        cabecera.Reposicion = request.Reposicion;
        SetMetodoEntrega(cabecera, metodoEntrega);
        cabecera.TotalVenta = totalVenta.TotalVenta;
        cabecera.MontoEnvioTransportadora = totalVenta.MontoEnvioTransportadora;

        var detallesParaEliminar = cabecera.Detalles
            .Where(x => !detalleIds.Contains(x.Id))
            .ToList();

        _context.VentasImpresionDet.RemoveRange(detallesParaEliminar);

        foreach (var detalleRequest in detalles)
        {
            var detalle = detalleRequest.Id.HasValue
                ? cabecera.Detalles.First(x => x.Id == detalleRequest.Id.Value)
                : new VentaImpresionDet { CabId = cabecera.Id };

            detalle.ProductoId = detalleRequest.ProductoId;
            detalle.TipoMaquinaId = detalleRequest.TipoMaquinaId;
            detalle.Cantidad = detalleRequest.Cantidad;
            detalle.PrecioUnitario = detalleRequest.PrecioUnitario;
            detalle.PrecioExtra = detalleRequest.PrecioExtra ?? 0;
            detalle.ArchivoDisenio = NormalizarRutaArchivo(detalleRequest.ArchivoDisenio);
            detalle.ArchivoDisenioNombre = detalleRequest.ArchivoDisenioNombre;
            detalle.Observacion = detalleRequest.Observacion;
            detalle.EstadoItem = string.IsNullOrWhiteSpace(detalleRequest.EstadoItem) ? EstadoDetalleInicial : detalleRequest.EstadoItem;
            detalle.CheckImpresion = detalleRequest.CheckImpresion ?? false;

            if (!detalleRequest.Id.HasValue)
            {
                _context.VentasImpresionDet.Add(detalle);
            }
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var venta = await QueryVentaCompleta()
            .AsNoTracking()
            .FirstAsync(x => x.Id == id);

        return venta.ToDto();
    }

    public async Task<VentaImpresionCabDto?> ActualizarCabeceraAsync(int id, VentaImpresionCabRequest request)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (cabecera is null)
        {
            return null;
        }

        if (EsActualizacionSoloPago(cabecera, request))
        {
            await ValidarCamposPagoAsync(request.FormaPagoId, request.MontoPagado, request.EstadoPagadoId, cabecera.TotalVenta);
            await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);

            cabecera.FormaPagoId = request.FormaPagoId;
            cabecera.MontoPagado = request.MontoPagado ?? 0;
            cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
            cabecera.ComprobantePago = NormalizarRutaArchivo(request.ComprobantePago);
            cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;

            await _context.SaveChangesAsync();

            var ventaSoloPago = await QueryVentaCompleta()
                .AsNoTracking()
                .FirstAsync(x => x.Id == id);

            return ventaSoloPago.ToDto();
        }

        await ValidarVentaEditableAsync(cabecera);
        await ValidarTransicionEstadoAsync(cabecera.EstadoVentaId, request.EstadoVentaId);
        await ValidarCabeceraAsync(
            request.ClienteId,
            request.FormaPagoId,
            request.VendedorId,
            request.MontoPagado,
            request.EstadoVentaId,
            request.EstadoPagadoId,
            request.TotalVenta);
        await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);
        await ValidarAdjuntosParaImpresionAsync(cabecera.EstadoVentaId, request.EstadoVentaId, cabecera.Detalles);
        var estadoVentaId = await ResolverEstadoVentaIdAsync(request.EstadoVentaId);

        cabecera.ClienteId = request.ClienteId;
        cabecera.FormaPagoId = request.FormaPagoId;
        cabecera.EstadoVentaId = estadoVentaId;
        cabecera.VendedorId = request.VendedorId;
        cabecera.MontoPagado = request.MontoPagado ?? 0;
        cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
        cabecera.FechaEntrega = request.FechaEntrega;
        cabecera.ComprobantePago = NormalizarRutaArchivo(request.ComprobantePago);
        cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;
        cabecera.Observacion = request.Observacion;
        SetMetodoEntrega(cabecera, request.MetodoEntrega);
        cabecera.MontoEnvioTransportadora = await MontoEnvioTransportadoraParaActualizacionAsync(cabecera, cabecera.MetodoEntrega);
        cabecera.TotalVenta = request.TotalVenta + cabecera.MontoEnvioTransportadora;

        await _context.SaveChangesAsync();

        var venta = await QueryVentaCompleta()
            .AsNoTracking()
            .FirstAsync(x => x.Id == id);

        return venta.ToDto();
    }

    public async Task<VentaImpresionCabDto?> MarcarVentaEliminadaAsync(int id, EliminarVentaImpresionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Observacion))
        {
            throw new InvalidOperationException("Debe agregar un comentario para eliminar el pedido.");
        }

        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (cabecera is null)
        {
            return null;
        }

        await ValidarVentaEliminableAsync(cabecera);

        var estadoEliminado = await ObtenerEstadoVentaActivoAsync(EstadoVentaEliminado, "No se encontro el estado eliminado.");

        cabecera.EstadoVentaId = estadoEliminado.Id;
        cabecera.Observacion = request.Observacion.Trim();

        await _context.SaveChangesAsync();

        var venta = await QueryVentaCompleta()
            .AsNoTracking()
            .FirstAsync(x => x.Id == id);

        return venta.ToDto();
    }

    public async Task<bool> EliminarVentaAsync(int id)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (cabecera is null)
        {
            return false;
        }

        await ValidarVentaEliminableAsync(cabecera);

        _context.VentasImpresionDet.RemoveRange(cabecera.Detalles);
        _context.VentasImpresionCab.Remove(cabecera);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<VentaImpresionDetDto> CrearDetalleAsync(int cabId, VentaImpresionDetalleCreateRequest request)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .FirstOrDefaultAsync(x => x.Id == cabId);
        if (cabecera is null)
        {
            throw new InvalidOperationException("La venta no existe.");
        }

        await ValidarVentaEditableAsync(cabecera);
        await ValidarDetalleAsync(request);
        var nuevoTotal = cabecera.TotalVenta + CalcularTotalDetalle(request.Cantidad, request.PrecioUnitario, request.PrecioExtra);
        await ValidarPagoAsync(cabecera.MontoPagado, cabecera.EstadoPagadoId, nuevoTotal);

        var detalle = new VentaImpresionDet
        {
            CabId = cabId,
            ProductoId = request.ProductoId,
            TipoMaquinaId = request.TipoMaquinaId,
            Cantidad = request.Cantidad,
            PrecioUnitario = request.PrecioUnitario,
            PrecioExtra = request.PrecioExtra ?? 0,
            ArchivoDisenio = NormalizarRutaArchivo(request.ArchivoDisenio),
            ArchivoDisenioNombre = request.ArchivoDisenioNombre,
            Observacion = request.Observacion,
            EstadoItem = string.IsNullOrWhiteSpace(request.EstadoItem) ? EstadoDetalleInicial : request.EstadoItem,
            CheckImpresion = request.CheckImpresion ?? false
        };

        _context.VentasImpresionDet.Add(detalle);
        await _context.SaveChangesAsync();
        await RecalcularTotalVentaAsync(cabId);

        var detalleGuardado = await QueryDetalle()
            .AsNoTracking()
            .FirstAsync(x => x.Id == detalle.Id);

        return detalleGuardado.ToDto();
    }

    public async Task<VentaImpresionDetDto?> ActualizarDetalleAsync(int cabId, int detalleId, VentaImpresionDetalleCreateRequest request)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .FirstOrDefaultAsync(x => x.Id == cabId);
        if (cabecera is null)
        {
            return null;
        }

        var detalle = await _context.VentasImpresionDet.FirstOrDefaultAsync(x => x.Id == detalleId && x.CabId == cabId);
        if (detalle is null)
        {
            return null;
        }

        await ValidarVentaEditableAsync(cabecera);
        await ValidarDetalleAsync(request);
        var totalAnterior = CalcularTotalDetalle(detalle.Cantidad, detalle.PrecioUnitario, detalle.PrecioExtra);
        var totalNuevoDetalle = CalcularTotalDetalle(request.Cantidad, request.PrecioUnitario, request.PrecioExtra);
        var nuevoTotal = cabecera.TotalVenta - totalAnterior + totalNuevoDetalle;
        await ValidarPagoAsync(cabecera.MontoPagado, cabecera.EstadoPagadoId, nuevoTotal);

        detalle.ProductoId = request.ProductoId;
        detalle.TipoMaquinaId = request.TipoMaquinaId;
        detalle.Cantidad = request.Cantidad;
        detalle.PrecioUnitario = request.PrecioUnitario;
        detalle.PrecioExtra = request.PrecioExtra ?? 0;
        detalle.ArchivoDisenio = NormalizarRutaArchivo(request.ArchivoDisenio);
        detalle.ArchivoDisenioNombre = request.ArchivoDisenioNombre;
        detalle.Observacion = request.Observacion;
        detalle.EstadoItem = string.IsNullOrWhiteSpace(request.EstadoItem) ? EstadoDetalleInicial : request.EstadoItem;
        detalle.CheckImpresion = request.CheckImpresion ?? false;

        await _context.SaveChangesAsync();
        await RecalcularTotalVentaAsync(cabId);

        var detalleActualizado = await QueryDetalle()
            .AsNoTracking()
            .FirstAsync(x => x.Id == detalleId);

        return detalleActualizado.ToDto();
    }

    public async Task<bool> EliminarDetalleAsync(int cabId, int detalleId)
    {
        var cabecera = await _context.VentasImpresionCab
            .Include(x => x.EstadoVenta)
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x => x.Id == cabId);
        if (cabecera is null)
        {
            return false;
        }

        var detalle = cabecera.Detalles.FirstOrDefault(x => x.Id == detalleId);
        if (detalle is null)
        {
            return false;
        }

        await ValidarVentaEditableAsync(cabecera);
        if (cabecera.Detalles.Count <= 1)
        {
            throw new InvalidOperationException("La venta debe tener al menos un detalle.");
        }

        _context.VentasImpresionDet.Remove(detalle);
        await _context.SaveChangesAsync();
        await RecalcularTotalVentaAsync(cabId);
        return true;
    }

    private async Task ValidarCabeceraAsync(VentaImpresionCompletaRequest request, decimal totalVenta)
    {
        await ValidarCabeceraAsync(
            request.ClienteId,
            request.FormaPagoId,
            request.VendedorId,
            request.MontoPagado,
            request.EstadoVentaId,
            request.EstadoPagadoId,
            totalVenta);
    }

    private async Task ValidarCabeceraAsync(VentaImpresionCompletaUpdateRequest request, decimal totalVenta)
    {
        await ValidarCabeceraAsync(
            request.ClienteId,
            request.FormaPagoId,
            request.VendedorId,
            request.MontoPagado,
            request.EstadoVentaId,
            request.EstadoPagadoId,
            totalVenta);
    }

    private async Task ValidarCabeceraAsync(
        int clienteId,
        string formaPagoId,
        int vendedorId,
        decimal? montoPagado,
        string? estadoVentaIdRequest,
        string? estadoPagadoIdRequest,
        decimal totalVenta)
    {
        if (clienteId <= 0)
        {
            throw new InvalidOperationException("Debe seleccionar un cliente.");
        }

        if (vendedorId <= 0)
        {
            throw new InvalidOperationException("Debe seleccionar un vendedor.");
        }

        if (string.IsNullOrWhiteSpace(formaPagoId))
        {
            throw new InvalidOperationException("Debe seleccionar una forma de pago.");
        }

        if (totalVenta < 0)
        {
            throw new InvalidOperationException("El total de la venta no puede ser negativo.");
        }

        if (!await _context.Clientes.AnyAsync(x => x.Id == clienteId && x.Estado != false))
        {
            throw new InvalidOperationException("El cliente no existe o esta inactivo.");
        }

        if (!await _context.FormasPago.AnyAsync(x => x.Id == formaPagoId && x.Estado == true))
        {
            throw new InvalidOperationException("La forma de pago no existe o esta inactiva.");
        }

        if (!await _context.Usuarios.AnyAsync(x => x.Id == vendedorId && x.Estado == true))
        {
            throw new InvalidOperationException("El vendedor no existe o esta inactivo.");
        }

        await ResolverEstadoVentaIdAsync(estadoVentaIdRequest);

        var estadoPagadoId = string.IsNullOrWhiteSpace(estadoPagadoIdRequest) ? EstadoPagoPendiente : estadoPagadoIdRequest;
        if (!await _context.EstadosPago.AnyAsync(x => x.Id == estadoPagadoId && x.Estado == true))
        {
            throw new InvalidOperationException("El estado de pago no existe o esta inactivo.");
        }

        if (montoPagado < 0)
        {
            throw new InvalidOperationException("El monto pagado no puede ser negativo.");
        }

        await ValidarPagoAsync(montoPagado, estadoPagadoId, totalVenta);
    }

    private async Task ValidarCamposPagoAsync(
        string formaPagoId,
        decimal? montoPagado,
        string? estadoPagadoIdRequest,
        decimal totalVenta)
    {
        if (string.IsNullOrWhiteSpace(formaPagoId))
        {
            throw new InvalidOperationException("Debe seleccionar una forma de pago.");
        }

        if (!await _context.FormasPago.AnyAsync(x => x.Id == formaPagoId && x.Estado == true))
        {
            throw new InvalidOperationException("La forma de pago no existe o esta inactiva.");
        }

        var estadoPagadoId = string.IsNullOrWhiteSpace(estadoPagadoIdRequest) ? EstadoPagoPendiente : estadoPagadoIdRequest;
        await ValidarPagoAsync(montoPagado, estadoPagadoId, totalVenta);
    }

    private async Task ValidarDetallesAsync(IEnumerable<VentaImpresionDetalleCreateRequest> detalles)
    {
        foreach (var detalle in detalles)
        {
            await ValidarDetalleAsync(detalle);
        }
    }

    private async Task ValidarDetallesAsync(IEnumerable<VentaImpresionDetalleUpdateRequest> detalles)
    {
        foreach (var detalle in detalles)
        {
            await ValidarDetalleAsync(detalle);
        }
    }

    private async Task ValidarDetalleAsync(VentaImpresionDetalleCreateRequest detalle)
    {
        await ValidarDetalleAsync(
            detalle.ProductoId,
            detalle.TipoMaquinaId,
            detalle.Cantidad,
            detalle.PrecioUnitario,
            detalle.PrecioExtra);
    }

    private async Task ValidarDetalleAsync(VentaImpresionDetalleUpdateRequest detalle)
    {
        await ValidarDetalleAsync(
            detalle.ProductoId,
            detalle.TipoMaquinaId,
            detalle.Cantidad,
            detalle.PrecioUnitario,
            detalle.PrecioExtra);
    }

    private async Task ValidarDetalleAsync(
        int productoId,
        int tipoMaquinaId,
        decimal cantidad,
        decimal precioUnitario,
        decimal? precioExtra)
    {
        if (cantidad <= 0)
        {
            throw new InvalidOperationException("La cantidad debe ser mayor a cero.");
        }

        if (precioUnitario < 0 || precioExtra < 0)
        {
            throw new InvalidOperationException("Los precios no pueden ser negativos.");
        }

        if (productoId <= 0)
        {
            throw new InvalidOperationException("Debe seleccionar un producto.");
        }

        if (tipoMaquinaId <= 0)
        {
            throw new InvalidOperationException("Debe seleccionar una maquina.");
        }

        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == productoId && x.Estado);
        if (producto is null)
        {
            throw new InvalidOperationException($"El producto {productoId} no existe o esta inactivo.");
        }

        if (!await _context.TiposMaquina.AnyAsync(x => x.Id == tipoMaquinaId && x.Estado))
        {
            throw new InvalidOperationException($"La maquina {tipoMaquinaId} no existe o esta inactiva.");
        }

        if (producto.MaquinaId.HasValue && producto.MaquinaId.Value != tipoMaquinaId)
        {
            throw new InvalidOperationException("La maquina seleccionada no corresponde al producto.");
        }
    }

    private async Task ValidarVentaEditableAsync(VentaImpresionCab cabecera)
    {
        var estado = await ObtenerEstadoVentaActivoAsync(cabecera.EstadoVentaId, "El estado de la venta no existe.");
        var estadoLimiteEditable = await ObtenerEstadoVentaActivoAsync(EstadoVentaLimiteEditable, "No se encontro el estado limite editable.");

        if (!EstadoDentroDelLimite(estado, estadoLimiteEditable))
        {
            throw new InvalidOperationException("La venta ya avanzo de estado y no puede modificarse.");
        }
    }

    private async Task ValidarVentaEliminableAsync(VentaImpresionCab cabecera)
    {
        var estado = await ObtenerEstadoVentaActivoAsync(cabecera.EstadoVentaId, "El estado de la venta no existe.");
        var estadoCarga = await ObtenerEstadoVentaActivoAsync(EstadoVentaCarga, "No se encontro el estado de carga.");

        if (!EsMismoEstado(estado, estadoCarga))
        {
            throw new InvalidOperationException("Solo se puede eliminar un pedido cuando esta en estado Carga.");
        }
    }

    private async Task ValidarTransicionEstadoAsync(string estadoActualId, string? estadoDestinoIdRequest)
    {
        var estadoDestinoId = await ResolverEstadoVentaIdAsync(estadoDestinoIdRequest);
        if (estadoActualId == estadoDestinoId)
        {
            return;
        }

        var actual = await ObtenerEstadoVentaActivoAsync(estadoActualId, "No se pudo validar el flujo de estados.");
        var destino = await ObtenerEstadoVentaActivoAsync(estadoDestinoId, "No se pudo validar el flujo de estados.");
        var estadoCarga = await ObtenerEstadoVentaActivoAsync(EstadoVentaCarga, "No se encontro el estado de carga.");
        var estadoLimiteEditable = await ObtenerEstadoVentaActivoAsync(EstadoVentaLimiteEditable, "No se encontro el estado limite editable.");

        var siguiente = await _estadoVentaFlujoService.ObtenerSiguienteAsync(actual, CancellationToken.None);
        var anterior = await _estadoVentaFlujoService.ObtenerAnteriorAsync(actual.Id, CancellationToken.None);

        var permitidoAvanzarCargaAImpresion = EsMismoEstado(actual, estadoCarga) && EsMismoEstado(siguiente, destino);
        var permitidoRetroceder = EstadoDentroDelLimite(actual, estadoLimiteEditable) && EsMismoEstado(anterior, destino);

        if (!permitidoAvanzarCargaAImpresion && !permitidoRetroceder)
        {
            throw new InvalidOperationException("Desde pedidos solo se puede avanzar de Carga a Impresion o devolver una venta al estado anterior.");
        }
    }

    private Task ValidarComprobantePagoAsync(string? estadoPagadoIdRequest, string? comprobantePago, string? comprobantePagoNombre)
    {
        var estadoPagadoId = string.IsNullOrWhiteSpace(estadoPagadoIdRequest) ? EstadoPagoPendiente : estadoPagadoIdRequest;
        if (estadoPagadoId == EstadoPagoPagado
            && string.IsNullOrWhiteSpace(comprobantePago)
            && string.IsNullOrWhiteSpace(comprobantePagoNombre))
        {
            throw new InvalidOperationException("Para marcar la venta como pagada debe adjuntar el comprobante de pago.");
        }

        return Task.CompletedTask;
    }

    private async Task ValidarAdjuntosParaImpresionAsync(
        string estadoActualId,
        string? estadoDestinoIdRequest,
        IEnumerable<VentaImpresionDetalleUpdateRequest> detalles)
    {
        if (!await EsTransicionCargaAImpresionAsync(estadoActualId, estadoDestinoIdRequest))
        {
            return;
        }

        if (detalles.Any(x => string.IsNullOrWhiteSpace(x.ArchivoDisenio) && string.IsNullOrWhiteSpace(x.ArchivoDisenioNombre)))
        {
            throw new InvalidOperationException("Para enviar a impresion debe adjuntar el diseno en todos los detalles.");
        }
    }

    private async Task ValidarAdjuntosParaImpresionAsync(
        string estadoActualId,
        string? estadoDestinoIdRequest,
        IEnumerable<VentaImpresionDet> detalles)
    {
        if (!await EsTransicionCargaAImpresionAsync(estadoActualId, estadoDestinoIdRequest))
        {
            return;
        }

        if (detalles.Any(x => string.IsNullOrWhiteSpace(x.ArchivoDisenio) && string.IsNullOrWhiteSpace(x.ArchivoDisenioNombre)))
        {
            throw new InvalidOperationException("Para enviar a impresion debe adjuntar el diseno en todos los detalles.");
        }
    }

    private async Task<bool> EsTransicionCargaAImpresionAsync(string estadoActualId, string? estadoDestinoIdRequest)
    {
        var estadoDestinoId = await ResolverEstadoVentaIdAsync(estadoDestinoIdRequest);
        if (estadoActualId == estadoDestinoId)
        {
            return false;
        }

        var actual = await ObtenerEstadoVentaActivoAsync(estadoActualId, "No se pudo validar el flujo de estados.");
        var destino = await ObtenerEstadoVentaActivoAsync(estadoDestinoId, "No se pudo validar el flujo de estados.");
        var estadoCarga = await ObtenerEstadoVentaActivoAsync(EstadoVentaCarga, "No se encontro el estado de carga.");
        var siguiente = await _estadoVentaFlujoService.ObtenerSiguienteAsync(actual, CancellationToken.None);

        return EsMismoEstado(actual, estadoCarga) && EsMismoEstado(siguiente, destino);
    }

    private async Task ValidarEstadoInicialAsync(string? estadoVentaIdRequest)
    {
        if (string.IsNullOrWhiteSpace(estadoVentaIdRequest))
        {
            return;
        }

        var estado = await ObtenerEstadoVentaActivoAsync(estadoVentaIdRequest, "El estado inicial de venta no existe o esta inactivo.");
        var estadoCarga = await ObtenerEstadoVentaActivoAsync(EstadoVentaCarga, "No se encontro el estado de carga.");

        if (!EsMismoEstado(estado, estadoCarga))
        {
            throw new InvalidOperationException("Una venta nueva debe iniciar en estado de carga.");
        }
    }

    private async Task<string> ResolverEstadoVentaIdAsync(string? estadoVentaIdRequest)
    {
        var estadoVentaId = string.IsNullOrWhiteSpace(estadoVentaIdRequest)
            ? EstadoVentaInicial
            : estadoVentaIdRequest.Trim();

        var estado = await ObtenerEstadoVentaActivoAsync(estadoVentaId, "El estado de venta no existe o esta inactivo.");
        return estado.Id;
    }

    private async Task<EstadoVenta> ObtenerEstadoVentaActivoAsync(string estadoVentaId, string mensajeError)
    {
        var estado = await _estadoVentaFlujoService.ObtenerPorIdAsync(estadoVentaId, CancellationToken.None);
        if (estado is null)
        {
            throw new InvalidOperationException(mensajeError);
        }

        return estado;
    }

    private static bool EsMismoEstado(EstadoVenta? primero, EstadoVenta? segundo)
    {
        return primero is not null
            && segundo is not null
            && string.Equals(primero.Id, segundo.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EstadoDentroDelLimite(EstadoVenta estado, EstadoVenta limite)
    {
        return estado.NumeroFlujo.HasValue
            && limite.NumeroFlujo.HasValue
            && estado.NumeroFlujo.Value <= limite.NumeroFlujo.Value;
    }

    private bool EsActualizacionSoloPago(
        VentaImpresionCab cabecera,
        VentaImpresionCompletaUpdateRequest request,
        decimal totalVentaRequest,
        IReadOnlyCollection<VentaImpresionDetalleUpdateRequest> detallesRequest)
    {
        var estadoVentaId = string.IsNullOrWhiteSpace(request.EstadoVentaId) ? EstadoVentaInicial : request.EstadoVentaId;

        return cabecera.ClienteId == request.ClienteId
            && cabecera.VendedorId == request.VendedorId
            && cabecera.TotalVenta == totalVentaRequest
            && ValoresIguales(cabecera.EstadoVentaId, estadoVentaId)
            && FechasIguales(cabecera.FechaEntrega, request.FechaEntrega)
            && ValoresIguales(cabecera.Observacion, request.Observacion)
            && cabecera.Reposicion == request.Reposicion
            && ValoresIguales(cabecera.MetodoEntrega, NormalizeMetodoEntrega(request.MetodoEntrega))
            && DetallesIguales(cabecera.Detalles, detallesRequest);
    }

    private bool EsActualizacionSoloPago(VentaImpresionCab cabecera, VentaImpresionCabRequest request)
    {
        return cabecera.ClienteId == request.ClienteId
            && cabecera.VendedorId == request.VendedorId
            && cabecera.TotalVenta == request.TotalVenta
            && ValoresIguales(cabecera.EstadoVentaId, request.EstadoVentaId)
            && FechasIguales(cabecera.FechaEntrega, request.FechaEntrega)
            && ValoresIguales(cabecera.Observacion, request.Observacion)
            && cabecera.Reposicion == request.Reposicion
            && ValoresIguales(cabecera.MetodoEntrega, NormalizeMetodoEntrega(request.MetodoEntrega));
    }

    private static bool DetallesIguales(
        ICollection<VentaImpresionDet> detallesActuales,
        IReadOnlyCollection<VentaImpresionDetalleUpdateRequest> detallesRequest)
    {
        if (detallesActuales.Count != detallesRequest.Count)
        {
            return false;
        }

        var actualesPorId = detallesActuales.ToDictionary(x => x.Id);
        foreach (var detalleRequest in detallesRequest)
        {
            if (!detalleRequest.Id.HasValue || !actualesPorId.TryGetValue(detalleRequest.Id.Value, out var actual))
            {
                return false;
            }

            if (actual.ProductoId != detalleRequest.ProductoId
                || actual.TipoMaquinaId != detalleRequest.TipoMaquinaId
                || actual.Cantidad != detalleRequest.Cantidad
                || actual.PrecioUnitario != detalleRequest.PrecioUnitario
                || (actual.PrecioExtra ?? 0) != (detalleRequest.PrecioExtra ?? 0)
                || !ValoresIguales(actual.ArchivoDisenio, detalleRequest.ArchivoDisenio)
                || !ValoresIguales(actual.ArchivoDisenioNombre, detalleRequest.ArchivoDisenioNombre)
                || !ValoresIguales(actual.Observacion, detalleRequest.Observacion)
                || !ValoresIguales(actual.EstadoItem, string.IsNullOrWhiteSpace(detalleRequest.EstadoItem) ? EstadoDetalleInicial : detalleRequest.EstadoItem)
                || (actual.CheckImpresion ?? false) != (detalleRequest.CheckImpresion ?? false))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FechasIguales(DateTime? actual, DateTime? request)
    {
        return actual?.Date == request?.Date;
    }

    private static bool ValoresIguales(string? actual, string? request)
    {
        return string.Equals(actual ?? string.Empty, request ?? string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeMetodoEntrega(string? metodoEntrega)
    {
        var normalized = (metodoEntrega ?? MetodoEntregaDelivery).Trim().ToUpperInvariant();
        return normalized switch
        {
            "DELIVERY" => "DELIVERY",
            "RETIRO_LOCAL" => "RETIRO_LOCAL",
            "MOTOBOLT" => "MOTOBOLT",
            "TRANSPORTADORA" => "TRANSPORTADORA",
            "OTRO" => "OTRO",
            _ => MetodoEntregaDelivery
        };
    }

    private static void SetMetodoEntrega(VentaImpresionCab cabecera, string? metodoEntrega)
    {
        cabecera.MetodoEntrega = NormalizeMetodoEntrega(metodoEntrega);
        if (!string.Equals(cabecera.MetodoEntrega, MetodoEntregaDelivery, StringComparison.OrdinalIgnoreCase))
        {
            cabecera.UsuarioEntregaPedidoId = null;
            cabecera.FechaTomaDelivery = null;
        }
    }

    private static string? NormalizarRutaArchivo(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return null;
        }

        var value = ruta.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El archivo debe guardarse en el servidor. En la venta solo se guarda la ruta del archivo.");
        }

        if (value.Length > 500)
        {
            throw new InvalidOperationException("La ruta del archivo no puede superar 500 caracteres.");
        }

        return value;
    }

    private async Task ValidarPagoAsync(decimal? montoPagadoRequest, string? estadoPagadoIdRequest, decimal totalVenta)
    {
        var montoPagado = montoPagadoRequest ?? 0;
        var estadoPagadoId = string.IsNullOrWhiteSpace(estadoPagadoIdRequest) ? EstadoPagoPendiente : estadoPagadoIdRequest;

        if (montoPagado < 0)
        {
            throw new InvalidOperationException("El monto pagado no puede ser negativo.");
        }

        if (montoPagado > totalVenta)
        {
            throw new InvalidOperationException("El monto pagado no puede ser mayor al total de la venta.");
        }

        if (estadoPagadoId == EstadoPagoPagado && montoPagado < totalVenta)
        {
            throw new InvalidOperationException("Para marcar la venta como pagada, el monto pagado debe cubrir el total.");
        }

        if (estadoPagadoId == EstadoPagoParcial && (montoPagado <= 0 || montoPagado >= totalVenta))
        {
            throw new InvalidOperationException("Para marcar la venta como parcial, el monto pagado debe ser mayor a cero y menor al total.");
        }

        if (estadoPagadoId == EstadoPagoPendiente && montoPagado > 0)
        {
            throw new InvalidOperationException("Si existe un monto pagado, el estado de pago debe ser Parcial o Pagado.");
        }

        if (estadoPagadoId == EstadoPagoPendiente && totalVenta > 0 && montoPagado == totalVenta)
        {
            throw new InvalidOperationException("Si el monto pagado cubre el total, el estado de pago debe ser Pagado.");
        }

        if (!await _context.EstadosPago.AnyAsync(x => x.Id == estadoPagadoId && x.Estado == true))
        {
            throw new InvalidOperationException("El estado de pago no existe o esta inactivo.");
        }
    }

    private async Task RecalcularTotalVentaAsync(int cabId)
    {
        var cabecera = await _context.VentasImpresionCab.FirstOrDefaultAsync(x => x.Id == cabId);
        if (cabecera is null)
        {
            return;
        }

        var totalDetalles = await _context.VentasImpresionDet
            .Where(x => x.CabId == cabId)
            .SumAsync(x => x.Cantidad * x.PrecioUnitario + (x.PrecioExtra ?? 0));
        cabecera.MontoEnvioTransportadora = await MontoEnvioTransportadoraAsync(cabecera.MetodoEntrega);
        cabecera.TotalVenta = totalDetalles + cabecera.MontoEnvioTransportadora;

        await _context.SaveChangesAsync();
    }

    private static decimal CalcularTotalDetalle(decimal cantidad, decimal precioUnitario, decimal? precioExtra)
    {
        return cantidad * precioUnitario + (precioExtra ?? 0);
    }

    private async Task<TotalVentaCalculado> CalcularTotalVentaAsync(IEnumerable<VentaImpresionDetalleCreateRequest> detalles, string? metodoEntrega)
    {
        var totalDetalles = detalles.Sum(x => CalcularTotalDetalle(x.Cantidad, x.PrecioUnitario, x.PrecioExtra));
        var montoEnvioTransportadora = await MontoEnvioTransportadoraAsync(metodoEntrega);
        return new TotalVentaCalculado(totalDetalles + montoEnvioTransportadora, montoEnvioTransportadora);
    }

    private async Task<TotalVentaCalculado> CalcularTotalVentaAsync(IEnumerable<VentaImpresionDetalleUpdateRequest> detalles, string? metodoEntrega)
    {
        var totalDetalles = detalles.Sum(x => CalcularTotalDetalle(x.Cantidad, x.PrecioUnitario, x.PrecioExtra));
        var montoEnvioTransportadora = await MontoEnvioTransportadoraAsync(metodoEntrega);
        return new TotalVentaCalculado(totalDetalles + montoEnvioTransportadora, montoEnvioTransportadora);
    }

    private async Task<TotalVentaCalculado> CalcularTotalVentaAsync(
        IEnumerable<VentaImpresionDetalleUpdateRequest> detalles,
        VentaImpresionCab cabecera,
        string? metodoEntrega)
    {
        var totalDetalles = detalles.Sum(x => CalcularTotalDetalle(x.Cantidad, x.PrecioUnitario, x.PrecioExtra));
        var montoEnvioTransportadora = await MontoEnvioTransportadoraParaActualizacionAsync(cabecera, metodoEntrega);
        return new TotalVentaCalculado(totalDetalles + montoEnvioTransportadora, montoEnvioTransportadora);
    }

    private async Task<decimal> MontoEnvioTransportadoraParaActualizacionAsync(VentaImpresionCab cabecera, string? metodoEntrega)
    {
        if (!string.Equals(NormalizeMetodoEntrega(metodoEntrega), MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(NormalizeMetodoEntrega(cabecera.MetodoEntrega), MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase)
            && cabecera.MontoEnvioTransportadora > 0)
        {
            return cabecera.MontoEnvioTransportadora;
        }

        return await MontoEnvioTransportadoraAsync(metodoEntrega);
    }

    private async Task<decimal> MontoEnvioTransportadoraAsync(string? metodoEntrega)
    {
        if (!string.Equals(NormalizeMetodoEntrega(metodoEntrega), MetodoEntregaTransportadora, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var monto = await _configuracionService.ObtenerValorIntAsync(
            ConfigMontoEnvioTransportadora,
            ConfigMontoEnvioTransportadoraNumero);

        if (monto.HasValue && monto.Value >= 0)
        {
            return monto.Value;
        }

        await _configuracionService.SaveAsync(new ConfiguracionRequest(
            ConfigMontoEnvioTransportadora,
            ConfigMontoEnvioTransportadoraNumero,
            MontoEnvioTransportadoraDefault.ToString()));

        return MontoEnvioTransportadoraDefault;
    }

    private readonly record struct TotalVentaCalculado(decimal TotalVenta, decimal MontoEnvioTransportadora);

    private IQueryable<VentaImpresionCab> QueryVentaCompleta()
    {
        return _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.FormaPago)
            .Include(x => x.EstadoPago)
            .Include(x => x.EstadoVenta)
            .Include(x => x.MetodoEnvio)
            .Include(x => x.UsuarioEntregaPedido).ThenInclude(x => x!.Persona)
            .Include(x => x.Detalles).ThenInclude(x => x.Producto)
            .Include(x => x.Detalles).ThenInclude(x => x.TipoMaquina);
    }

    private IQueryable<VentaImpresionDet> QueryDetalle()
    {
        return _context.VentasImpresionDet
            .Include(x => x.Producto)
            .Include(x => x.TipoMaquina);
    }
}
