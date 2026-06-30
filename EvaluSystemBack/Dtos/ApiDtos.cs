using System.ComponentModel.DataAnnotations;

namespace EvaluSystemBack.Dtos;

public record CatalogDto(string Id, string? Nombre, bool? Estado);

public record EstadoVentaOptionDto(string Id, string? Nombre, string? Estado, int? NumeroFlujo);

public record DepartamentoDto(int Id, string Nombre, bool Estado);

public record CiudadDto(int Id, int DepartamentoId, string? Departamento, int CodigoDistrito, string Nombre, bool Estado);

public record DepartamentoRequest(int? Id, [Required] string Nombre, bool Estado);

public record CiudadRequest(
    [Range(1, int.MaxValue)] int DepartamentoId,
    int? CodigoDistrito,
    [Required] string Nombre,
    bool Estado);

public record ClienteDto(
    int Id,
    string? Nombre,
    string? Documento,
    string TipoDocumentoId,
    string TipoClienteId,
    string? Email,
    string? NroTelefono,
    string? Direccion,
    int? DepartamentoId,
    string? Departamento,
    int? CiudadId,
    string? Ciudad,
    bool? Estado,
    ClienteDatosEnvioDto? DatosEnvio);

public record ClienteRequest(
    [Required] string? Nombre,
    string? Documento,
    string? TipoDocumentoId,
    [Required] string TipoClienteId,
    [EmailAddress] 
    string? Email,
    [Required] string? NroTelefono,
    string? Direccion,
    int? DepartamentoId,
    int? CiudadId,
    bool? Estado);

public record ClienteDatosEnvioDto(
    int Id,
    int ClienteId,
    int TransportadoraId,
    string? Transportadora,
    string NombreReceptor,
    string DocumentoReceptor,
    string TelefonoReceptor,
    int DepartamentoId,
    string? Departamento,
    int CiudadId,
    string? Ciudad,
    string Direccion,
    string? Observacion,
    bool Estado);

public record ClienteDatosEnvioRequest(
    int ClienteId,
    [Range(1, int.MaxValue)]
    int TransportadoraId,
    [Required] string NombreReceptor,
    [Required] string DocumentoReceptor,
    [Required] string TelefonoReceptor,
    [Range(1, int.MaxValue)]
    int DepartamentoId,
    [Range(1, int.MaxValue)]
    int CiudadId,
    [Required] string Direccion,
    string? Observacion,
    bool Estado);

public record TransportadoraDto(int Id, string Nombre, string? Telefono, string? Direccion, string? Observacion, bool Estado);

public record TransportadoraRequest([Required] string Nombre, string? Telefono, string? Direccion, string? Observacion, bool Estado);

public record PersonaDto(
    int Id,
    int? PerfilId,
    string? Perfil,
    string? PrimerNombre,
    string? SegundoNombre,
    string? PrimerApellido,
    string? SegundoApellido,
    DateTime? FechaCumpleanios,
    string? TipoDocumentoId,
    string? Documento,
    bool? Estado);

public record PersonaRequest(
    int? PerfilId,
    string? PrimerNombre,
    string? SegundoNombre,
    string? PrimerApellido,
    string? SegundoApellido,
    DateTime? FechaCumpleanios,
    string? TipoDocumentoId,
    string? Documento,
    bool? Estado);

public record ProductoDto(int Id, string Nombre, decimal PrecioBase, decimal? Comision, int? MaquinaId, string? Maquina, bool Estado);

public record ProductoRequest([Required] string Nombre, [Range(0, double.MaxValue)] decimal PrecioBase, [Range(0, double.MaxValue)] decimal? Comision, int? MaquinaId, bool Estado);

public record PerfilDto(int Id, string Nombre, string? Descripcion, bool Estado);

public record PerfilRequest([Required] string Nombre, string? Descripcion, bool Estado);

public record TipoMaquinaDto(int Id, string Nombre, bool Estado);

public record TipoMaquinaRequest([Required] string Nombre, bool Estado);

public record UsuarioDto(
    int Id,
    string? NombreUsuario,
    int? PersonaId,
    string? Persona,
    int? PerfilId,
    string? Perfil,
    IEnumerable<int> PerfilIds,
    string? Perfiles,
    bool? Estado);

public record UsuarioRequest([Required] string? NombreUsuario, string? Pass, int? PersonaId, IEnumerable<int>? PerfilIds, bool? Estado);

public record VentaImpresionOptionsDto(
    IEnumerable<ClienteDto> Clientes,
    IEnumerable<CatalogDto> FormasPago,
    IEnumerable<UsuarioDto> Vendedores,
    IEnumerable<CatalogDto> EstadosPago,
    IEnumerable<EstadoVentaOptionDto> EstadosVenta,
    IEnumerable<ProductoDto> Productos,
    IEnumerable<TipoMaquinaDto> Maquinas,
    int? UsuarioActualId,
    bool PuedeVerTodosPedidos);

public record ExcelFileDto(string FileName, string ContentType, string Bytes);

public record VentaImpresionCabDto(
    int Id,
    int ClienteId,
    string? Cliente,
    string FormaPagoId,
    string? FormaPago,
    decimal TotalVenta,
    string EstadoVentaId,
    string? EstadoVenta,
    int VendedorId,
    decimal? MontoPagado,
    string? EstadoPagadoId,
    string? EstadoPagado,
    DateTime FechaCreacion,
    DateTime? FechaEntrega,
    string? ComprobantePago,
    string? ComprobantePagoNombre,
    string? Observacion,
    string? MetodoEntrega,
    int? DeliveryUsuarioId,
    string? DeliveryUsuario,
    DateTime? FechaTomaDelivery,
    IEnumerable<VentaImpresionDetDto> Detalles);

public record VentaImpresionCabRequest(
    int ClienteId,
    [Required]
    [StringLength(1)]
    string FormaPagoId,
    [Range(0, double.MaxValue)] decimal TotalVenta,
    [Required]
    [StringLength(2)]
    string EstadoVentaId,
    int VendedorId,
    decimal? MontoPagado,
    [StringLength(50)]
    string? EstadoPagadoId,
    DateTime? FechaEntrega,
    [StringLength(500)]
    string? ComprobantePago,
    [StringLength(255)]
    string? ComprobantePagoNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(30)]
    string? MetodoEntrega);

public record EliminarVentaImpresionRequest(
    [Required]
    [StringLength(500)]
    string Observacion);

public record DeliveryPedidoDto(
    int Id,
    DateTime FechaCreacion,
    DateTime? FechaEntrega,
    string Cliente,
    string? Telefono,
    string? Direccion,
    string? Departamento,
    string? Ciudad,
    string EstadoVentaId,
    string? EstadoVenta,
    string Vendedor,
    decimal TotalVenta,
    string MetodoEntregaId,
    string MetodoEntrega,
    int? DeliveryUsuarioId,
    string? DeliveryUsuario,
    DateTime? FechaTomaDelivery,
    string Productos);

public record DeliveryResumenDto(
    int UsuarioId,
    string Delivery,
    int CantidadPedidos,
    decimal TotalPedidos,
    string Ciudades,
    string MetodosEnvio);

public record VentaImpresionDetDto(
    int Id,
    int CabId,
    int ProductoId,
    string? Producto,
    int TipoMaquinaId,
    string? TipoMaquina,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal? PrecioExtra,
    decimal? PrecioTotal,
    string? ArchivoDisenio,
    string? ArchivoDisenioNombre,
    string? Observacion,
    string EstadoItem,
    bool? CheckImpresion);

public record ImpresionArchivoDto(
    int DetalleId,
    int PedidoId,
    DateTime FechaCarga,
    DateTime? FechaEntrega,
    string Cliente,
    int TipoMaquinaId,
    string TipoMaquina,
    string Producto,
    decimal Cantidad,
    string? ArchivoDisenioNombre,
    string EstadoVenta,
    bool Impreso);

public record ImpresionMarcarDto(
    int DetalleId,
    int PedidoId,
    bool DetalleImpreso,
    bool PedidoCompleto,
    string EstadoVentaId,
    string? EstadoVenta);

public record VentaImpresionDetRequest(
    int CabId,
    int ProductoId,
    int TipoMaquinaId,
    [Range(0.01, double.MaxValue)] decimal Cantidad,
    [Range(0, double.MaxValue)] decimal PrecioUnitario,
    [Range(0, double.MaxValue)] decimal? PrecioExtra,
    [StringLength(500)]
    string? ArchivoDisenio,
    [StringLength(255)]
    string? ArchivoDisenioNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(2)]
    [Required] string EstadoItem,
    bool? CheckImpresion);

public record VentaImpresionCompletaRequest(
    int ClienteId,
    [Required]
    [StringLength(1)]
    string FormaPagoId,
    int VendedorId,
    decimal? MontoPagado,
    [StringLength(50)]
    string? EstadoPagadoId,
    DateTime? FechaEntrega,
    [StringLength(500)]
    string? ComprobantePago,
    [StringLength(255)]
    string? ComprobantePagoNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(30)]
    string? MetodoEntrega,
    [StringLength(2)]
    string? EstadoVentaId,
    [Required] IEnumerable<VentaImpresionDetalleCreateRequest> Detalles);

public record VentaImpresionDetalleCreateRequest(
    int ProductoId,
    int TipoMaquinaId,
    [Range(0.01, double.MaxValue)] decimal Cantidad,
    [Range(0, double.MaxValue)] decimal PrecioUnitario,
    [Range(0, double.MaxValue)] decimal? PrecioExtra,
    [StringLength(500)]
    string? ArchivoDisenio,
    [StringLength(255)]
    string? ArchivoDisenioNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(2)]
    string? EstadoItem,
    bool? CheckImpresion);

public record VentaImpresionCompletaUpdateRequest(
    int ClienteId,
    [Required]
    [StringLength(1)]
    string FormaPagoId,
    int VendedorId,
    decimal? MontoPagado,
    [StringLength(50)]
    string? EstadoPagadoId,
    DateTime? FechaEntrega,
    [StringLength(500)]
    string? ComprobantePago,
    [StringLength(255)]
    string? ComprobantePagoNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(30)]
    string? MetodoEntrega,
    [StringLength(2)]
    string? EstadoVentaId,
    [Required] IEnumerable<VentaImpresionDetalleUpdateRequest> Detalles);

public record VentaImpresionDetalleUpdateRequest(
    int? Id,
    int ProductoId,
    int TipoMaquinaId,
    [Range(0.01, double.MaxValue)] decimal Cantidad,
    [Range(0, double.MaxValue)] decimal PrecioUnitario,
    [Range(0, double.MaxValue)] decimal? PrecioExtra,
    [StringLength(500)]
    string? ArchivoDisenio,
    [StringLength(255)]
    string? ArchivoDisenioNombre,
    [StringLength(500)]
    string? Observacion,
    [StringLength(2)]
    string? EstadoItem,
    bool? CheckImpresion);
