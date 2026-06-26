using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class VentaImpresionService : IVentaImpresionService
{
    private const string EstadoVentaInicial = "PI";
    private const string EstadoDetalleInicial = "IP";
    private const string EstadoPagoPendiente = "P1";
    private const string EstadoPagoParcial = "P2";
    private const string EstadoPagoPagado = "P3";
    private const int FlujoCarga = 1;
    private const int FlujoImpresion = 2;
    private const int FlujoEnvio = 3;
    private const int FlujoEnviado = 4;
    private const int FlujoEliminado = 5;
    private const int UltimoFlujoEditable = FlujoEnvio;

    private readonly EvaluSystemDbContext _context;

    public VentaImpresionService(EvaluSystemDbContext context)
    {
        _context = context;
    }

    public async Task<VentaImpresionCabDto> CrearVentaCompletaAsync(VentaImpresionCompletaRequest request)
    {
        var detalles = request.Detalles?.ToList() ?? new List<VentaImpresionDetalleCreateRequest>();
        if (detalles.Count == 0)
        {
            throw new InvalidOperationException("La venta debe tener al menos un detalle.");
        }

        await ValidarDetallesAsync(detalles);

        var totalVenta = detalles.Sum(x => CalcularTotalDetalle(x.Cantidad, x.PrecioUnitario, x.PrecioExtra));
        await ValidarEstadoInicialAsync(request.EstadoVentaId);
        await ValidarCabeceraAsync(request, totalVenta);
        await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var cabecera = new VentaImpresionCab
        {
            ClienteId = request.ClienteId,
            FormaPagoId = request.FormaPagoId,
            TotalVenta = totalVenta,
            EstadoVentaId = string.IsNullOrWhiteSpace(request.EstadoVentaId) ? EstadoVentaInicial : request.EstadoVentaId,
            VendedorId = request.VendedorId,
            MontoPagado = request.MontoPagado ?? 0,
            EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId,
            FechaEntrega = request.FechaEntrega,
            ComprobantePago = request.ComprobantePago,
            ComprobantePagoNombre = request.ComprobantePagoNombre,
            Observacion = request.Observacion
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
                ArchivoDisenio = detalleRequest.ArchivoDisenio,
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

        var totalVenta = detalles.Sum(x => CalcularTotalDetalle(x.Cantidad, x.PrecioUnitario, x.PrecioExtra));
        if (EsActualizacionSoloPago(cabecera, request, totalVenta, detalles))
        {
            await ValidarCamposPagoAsync(request.FormaPagoId, request.MontoPagado, request.EstadoPagadoId, cabecera.TotalVenta);
            await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);

            cabecera.FormaPagoId = request.FormaPagoId;
            cabecera.MontoPagado = request.MontoPagado ?? 0;
            cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
            cabecera.ComprobantePago = request.ComprobantePago;
            cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;

            await _context.SaveChangesAsync();

            var ventaSoloPago = await QueryVentaCompleta()
                .AsNoTracking()
                .FirstAsync(x => x.Id == id);

            return ventaSoloPago.ToDto();
        }

        await ValidarDetallesAsync(detalles);
        await ValidarCabeceraAsync(request, totalVenta);
        await ValidarComprobantePagoAsync(request.EstadoPagadoId, request.ComprobantePago, request.ComprobantePagoNombre);
        await ValidarVentaEditableAsync(cabecera);
        await ValidarTransicionEstadoAsync(cabecera.EstadoVentaId, request.EstadoVentaId);
        await ValidarAdjuntosParaImpresionAsync(cabecera.EstadoVentaId, request.EstadoVentaId, detalles);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        cabecera.ClienteId = request.ClienteId;
        cabecera.FormaPagoId = request.FormaPagoId;
        cabecera.EstadoVentaId = string.IsNullOrWhiteSpace(request.EstadoVentaId) ? EstadoVentaInicial : request.EstadoVentaId;
        cabecera.VendedorId = request.VendedorId;
        cabecera.MontoPagado = request.MontoPagado ?? 0;
        cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
        cabecera.FechaEntrega = request.FechaEntrega;
        cabecera.ComprobantePago = request.ComprobantePago;
        cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;
        cabecera.Observacion = request.Observacion;
        cabecera.TotalVenta = totalVenta;

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
            detalle.ArchivoDisenio = detalleRequest.ArchivoDisenio;
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
            cabecera.ComprobantePago = request.ComprobantePago;
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

        cabecera.ClienteId = request.ClienteId;
        cabecera.FormaPagoId = request.FormaPagoId;
        cabecera.TotalVenta = request.TotalVenta;
        cabecera.EstadoVentaId = request.EstadoVentaId;
        cabecera.VendedorId = request.VendedorId;
        cabecera.MontoPagado = request.MontoPagado ?? 0;
        cabecera.EstadoPagadoId = string.IsNullOrWhiteSpace(request.EstadoPagadoId) ? EstadoPagoPendiente : request.EstadoPagadoId;
        cabecera.FechaEntrega = request.FechaEntrega;
        cabecera.ComprobantePago = request.ComprobantePago;
        cabecera.ComprobantePagoNombre = request.ComprobantePagoNombre;
        cabecera.Observacion = request.Observacion;

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

        await ValidarVentaEditableAsync(cabecera);

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
            ArchivoDisenio = request.ArchivoDisenio,
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
        detalle.ArchivoDisenio = request.ArchivoDisenio;
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

        var estadoVentaId = string.IsNullOrWhiteSpace(estadoVentaIdRequest) ? EstadoVentaInicial : estadoVentaIdRequest;
        if (!await _context.EstadosVenta.AnyAsync(x => x.Id == estadoVentaId && x.Estado == "A"))
        {
            throw new InvalidOperationException("El estado de venta no existe o esta inactivo.");
        }

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
        var estado = cabecera.EstadoVenta ?? await _context.EstadosVenta
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cabecera.EstadoVentaId);

        if (estado is null)
        {
            throw new InvalidOperationException("El estado de la venta no existe.");
        }

        if (!string.Equals(estado.Estado, "A", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El estado actual de la venta esta inactivo.");
        }

        if ((estado.NumeroFlujo ?? int.MaxValue) > UltimoFlujoEditable)
        {
            throw new InvalidOperationException("La venta ya avanzo de estado y no puede modificarse.");
        }
    }

    private async Task ValidarTransicionEstadoAsync(string estadoActualId, string? estadoDestinoIdRequest)
    {
        var estadoDestinoId = string.IsNullOrWhiteSpace(estadoDestinoIdRequest) ? EstadoVentaInicial : estadoDestinoIdRequest;
        if (estadoActualId == estadoDestinoId)
        {
            return;
        }

        var estados = await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => x.Id == estadoActualId || x.Id == estadoDestinoId)
            .ToListAsync();

        var actual = estados.FirstOrDefault(x => x.Id == estadoActualId);
        var destino = estados.FirstOrDefault(x => x.Id == estadoDestinoId);

        if (actual is null || destino is null)
        {
            throw new InvalidOperationException("No se pudo validar el flujo de estados.");
        }

        var flujoActual = actual.NumeroFlujo;
        var flujoDestino = destino.NumeroFlujo;

        var permitido = (flujoActual, flujoDestino) switch
        {
            (FlujoCarga, FlujoImpresion) => true,
            (FlujoImpresion, FlujoCarga) => true,
            (FlujoImpresion, FlujoEnvio) => true,
            (FlujoEnvio, FlujoEnviado) => true,
            (_, FlujoEliminado) when flujoActual <= FlujoEnvio => true,
            _ => false
        };

        if (!permitido)
        {
            throw new InvalidOperationException("El cambio de estado solicitado no corresponde al flujo permitido.");
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
        var estadoDestinoId = string.IsNullOrWhiteSpace(estadoDestinoIdRequest) ? EstadoVentaInicial : estadoDestinoIdRequest;
        if (estadoActualId == estadoDestinoId)
        {
            return false;
        }

        var estados = await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => x.Id == estadoActualId || x.Id == estadoDestinoId)
            .ToListAsync();

        var actual = estados.FirstOrDefault(x => x.Id == estadoActualId);
        var destino = estados.FirstOrDefault(x => x.Id == estadoDestinoId);

        return actual?.NumeroFlujo == FlujoCarga && destino?.NumeroFlujo == FlujoImpresion;
    }

    private async Task ValidarEstadoInicialAsync(string? estadoVentaIdRequest)
    {
        if (string.IsNullOrWhiteSpace(estadoVentaIdRequest))
        {
            return;
        }

        var estado = await _context.EstadosVenta
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == estadoVentaIdRequest);

        if (estado is null || !string.Equals(estado.Estado, "A", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El estado inicial de venta no existe o esta inactivo.");
        }

        if (estado.NumeroFlujo != FlujoCarga)
        {
            throw new InvalidOperationException("Una venta nueva debe iniciar en estado de carga.");
        }
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
            && DetallesIguales(cabecera.Detalles, detallesRequest);
    }

    private bool EsActualizacionSoloPago(VentaImpresionCab cabecera, VentaImpresionCabRequest request)
    {
        return cabecera.ClienteId == request.ClienteId
            && cabecera.VendedorId == request.VendedorId
            && cabecera.TotalVenta == request.TotalVenta
            && ValoresIguales(cabecera.EstadoVentaId, request.EstadoVentaId)
            && FechasIguales(cabecera.FechaEntrega, request.FechaEntrega)
            && ValoresIguales(cabecera.Observacion, request.Observacion);
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

        cabecera.TotalVenta = await _context.VentasImpresionDet
            .Where(x => x.CabId == cabId)
            .SumAsync(x => x.Cantidad * x.PrecioUnitario + (x.PrecioExtra ?? 0));

        await _context.SaveChangesAsync();
    }

    private static decimal CalcularTotalDetalle(decimal cantidad, decimal precioUnitario, decimal? precioExtra)
    {
        return cantidad * precioUnitario + (precioExtra ?? 0);
    }

    private IQueryable<VentaImpresionCab> QueryVentaCompleta()
    {
        return _context.VentasImpresionCab
            .Include(x => x.Cliente)
            .Include(x => x.FormaPago)
            .Include(x => x.EstadoPago)
            .Include(x => x.EstadoVenta)
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
