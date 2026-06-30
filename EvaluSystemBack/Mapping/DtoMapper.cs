using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;

namespace EvaluSystemBack.Mapping;

public static class DtoMapper
{
    public static CatalogDto ToDto(this SimpleStringCatalog entity)
    {
        return new CatalogDto(entity.Id, entity.Nombre, entity.Estado);
    }

    public static EstadoVentaOptionDto ToDto(this EstadoVenta entity)
    {
        return new EstadoVentaOptionDto(entity.Id, entity.Nombre, entity.Estado, entity.NumeroFlujo);
    }

    public static CatalogDto ToDto(this FormaPago entity)
    {
        return new CatalogDto(entity.Id, entity.Nombre, entity.Estado);
    }

    public static DepartamentoDto ToDto(this Departamento entity)
    {
        return new DepartamentoDto(entity.Id, entity.Nombre, entity.Estado);
    }

    public static CiudadDto ToDto(this Ciudad entity)
    {
        return new CiudadDto(entity.Id, entity.DepartamentoId, entity.Departamento?.Nombre, entity.CodigoDistrito, entity.Nombre, entity.Estado);
    }

    public static ClienteDto ToDto(this Cliente entity)
    {
        return new ClienteDto(
            entity.Id,
            entity.Nombre,
            entity.Documento,
            entity.TipoDocumentoId,
            entity.TipoClienteId,
            entity.Email,
            entity.NroTelefono,
            entity.Direccion,
            entity.DepartamentoId,
            entity.Departamento?.Nombre,
            entity.CiudadId,
            entity.Ciudad?.Nombre,
            entity.Estado,
            entity.DatosEnvio?.ToDto());
    }

    public static Cliente ToEntity(this ClienteRequest request, Cliente? entity = null)
    {
        entity ??= new Cliente();
        entity.Nombre = request.Nombre;
        entity.Documento = request.Documento;
        entity.TipoDocumentoId = string.IsNullOrWhiteSpace(request.TipoDocumentoId) ? "CI" : request.TipoDocumentoId;
        entity.TipoClienteId = request.TipoClienteId;
        entity.Email = request.Email;
        entity.NroTelefono = request.NroTelefono;
        entity.Direccion = request.Direccion;
        entity.DepartamentoId = request.DepartamentoId;
        entity.CiudadId = request.CiudadId;
        entity.Estado = request.Estado;
        return entity;
    }

    public static ClienteDatosEnvioDto ToDto(this ClienteDatosEnvio entity)
    {
        return new ClienteDatosEnvioDto(
            entity.Id,
            entity.ClienteId,
            entity.TransportadoraId,
            entity.Transportadora?.Nombre,
            entity.NombreReceptor,
            entity.DocumentoReceptor,
            entity.TelefonoReceptor,
            entity.DepartamentoId,
            entity.Departamento?.Nombre,
            entity.CiudadId,
            entity.Ciudad?.Nombre,
            entity.Direccion,
            entity.Observacion,
            entity.Estado);
    }

    public static ClienteDatosEnvio ToEntity(this ClienteDatosEnvioRequest request, ClienteDatosEnvio? entity = null)
    {
        entity ??= new ClienteDatosEnvio();
        entity.ClienteId = request.ClienteId;
        entity.TransportadoraId = request.TransportadoraId;
        entity.NombreReceptor = request.NombreReceptor;
        entity.DocumentoReceptor = request.DocumentoReceptor;
        entity.TelefonoReceptor = request.TelefonoReceptor;
        entity.DepartamentoId = request.DepartamentoId;
        entity.CiudadId = request.CiudadId;
        entity.Direccion = request.Direccion;
        entity.Observacion = request.Observacion;
        entity.Estado = request.Estado;
        return entity;
    }

    public static ConfiguracionDto ToDto(this Configuracion entity)
    {
        return new ConfiguracionDto(entity.Id, entity.Nombre, entity.NroConfiguracion, entity.Valor);
    }

    public static Configuracion ToEntity(this ConfiguracionRequest request, Configuracion? entity = null)
    {
        entity ??= new Configuracion();
        entity.Nombre = request.Nombre;
        entity.NroConfiguracion = request.NroConfiguracion;
        entity.Valor = request.Valor;
        return entity;
    }

    public static TransportadoraDto ToDto(this Transportadora entity)
    {
        return new TransportadoraDto(entity.Id, entity.Nombre, entity.Telefono, entity.Direccion, entity.Observacion, entity.Estado);
    }

    public static Transportadora ToEntity(this TransportadoraRequest request, Transportadora? entity = null)
    {
        entity ??= new Transportadora();
        entity.Nombre = request.Nombre;
        entity.Telefono = request.Telefono;
        entity.Direccion = request.Direccion;
        entity.Observacion = request.Observacion;
        entity.Estado = request.Estado;
        return entity;
    }

    public static PersonaDto ToDto(this Persona entity)
    {
        return new PersonaDto(
            entity.Id,
            entity.PerfilId,
            entity.Perfil?.Nombre,
            entity.PrimerNombre,
            entity.SegundoNombre,
            entity.PrimerApellido,
            entity.SegundoApellido,
            entity.FechaCumpleanios,
            entity.TipoDocumentoId,
            entity.Documento,
            entity.Estado);
    }

    public static Persona ToEntity(this PersonaRequest request, Persona? entity = null)
    {
        entity ??= new Persona();
        entity.PerfilId = request.PerfilId;
        entity.PrimerNombre = request.PrimerNombre;
        entity.SegundoNombre = request.SegundoNombre;
        entity.PrimerApellido = request.PrimerApellido;
        entity.SegundoApellido = request.SegundoApellido;
        entity.FechaCumpleanios = request.FechaCumpleanios;
        entity.TipoDocumentoId = request.TipoDocumentoId;
        entity.Documento = request.Documento;
        entity.Estado = request.Estado;
        return entity;
    }

    public static ProductoDto ToDto(this Producto entity)
    {
        return new ProductoDto(entity.Id, entity.Nombre, entity.PrecioBase, entity.Comision, entity.MaquinaId, entity.TipoMaquina?.Nombre, entity.Estado);
    }

    public static Producto ToEntity(this ProductoRequest request, Producto? entity = null)
    {
        entity ??= new Producto();
        entity.Nombre = request.Nombre;
        entity.PrecioBase = request.PrecioBase;
        entity.Comision = request.Comision;
        entity.MaquinaId = request.MaquinaId;
        entity.Estado = request.Estado;
        return entity;
    }

    public static PerfilDto ToDto(this Perfil entity)
    {
        return new PerfilDto(entity.Id, entity.Nombre, entity.Descripcion, entity.Estado);
    }

    public static Perfil ToEntity(this PerfilRequest request, Perfil? entity = null)
    {
        entity ??= new Perfil();
        entity.Nombre = request.Nombre;
        entity.Descripcion = request.Descripcion;
        entity.Estado = request.Estado;
        return entity;
    }

    public static TipoMaquinaDto ToDto(this TipoMaquina entity)
    {
        return new TipoMaquinaDto(entity.Id, entity.Nombre, entity.Estado);
    }

    public static TipoMaquina ToEntity(this TipoMaquinaRequest request, TipoMaquina? entity = null)
    {
        entity ??= new TipoMaquina();
        entity.Nombre = request.Nombre;
        entity.Estado = request.Estado;
        return entity;
    }

    public static UsuarioDto ToDto(this Usuario entity)
    {
        var persona = string.Join(" ", new[]
        {
            entity.Persona?.PrimerNombre,
            entity.Persona?.SegundoNombre,
            entity.Persona?.PrimerApellido,
            entity.Persona?.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var perfiles = entity.Perfiles
            .Where(x => x.Estado && x.Perfil != null)
            .OrderBy(x => x.Perfil!.Nombre)
            .ToList();
        var perfilIds = perfiles.Select(x => x.PerfilId).ToList();
        var perfilNombres = string.Join(", ", perfiles.Select(x => x.Perfil!.Nombre));
        var legacyPerfilId = entity.Persona?.PerfilId;
        var legacyPerfil = entity.Persona?.Perfil?.Nombre;

        return new UsuarioDto(
            entity.Id,
            entity.NombreUsuario,
            entity.PersonaId,
            string.IsNullOrWhiteSpace(persona) ? null : persona,
            perfilIds.FirstOrDefault() == 0 ? legacyPerfilId : perfilIds.First(),
            string.IsNullOrWhiteSpace(perfilNombres) ? legacyPerfil : perfilNombres,
            perfilIds.Count > 0 ? perfilIds : legacyPerfilId.HasValue ? new[] { legacyPerfilId.Value } : Array.Empty<int>(),
            string.IsNullOrWhiteSpace(perfilNombres) ? legacyPerfil : perfilNombres,
            entity.Estado);
    }

    public static Usuario ToEntity(this UsuarioRequest request, Usuario? entity = null)
    {
        entity ??= new Usuario();
        entity.NombreUsuario = request.NombreUsuario;
        entity.PersonaId = request.PersonaId;
        entity.Estado = request.Estado;
        return entity;
    }

    public static VentaImpresionCabDto ToDto(this VentaImpresionCab entity)
    {
        return new VentaImpresionCabDto(
            entity.Id,
            entity.ClienteId,
            entity.Cliente?.Nombre,
            entity.FormaPagoId,
            entity.FormaPago?.Nombre,
            entity.TotalVenta,
            entity.EstadoVentaId,
            entity.EstadoVenta?.Nombre,
            entity.VendedorId,
            entity.MontoPagado,
            entity.EstadoPagadoId,
            entity.EstadoPago?.Nombre,
            entity.FechaCreacion,
            entity.FechaEntrega,
            entity.ComprobantePago,
            entity.ComprobantePagoNombre,
            entity.Observacion,
            entity.MetodoEntrega,
            entity.UsuarioEntregaPedidoId,
            entity.UsuarioEntregaPedido is null ? null : NombreUsuario(entity.UsuarioEntregaPedido),
            entity.FechaTomaDelivery,
            entity.Detalles.Select(x => x.ToDto()));
    }

    public static VentaImpresionCab ToEntity(this VentaImpresionCabRequest request, VentaImpresionCab? entity = null)
    {
        entity ??= new VentaImpresionCab();
        entity.ClienteId = request.ClienteId;
        entity.FormaPagoId = request.FormaPagoId;
        entity.TotalVenta = request.TotalVenta;
        entity.EstadoVentaId = request.EstadoVentaId;
        entity.VendedorId = request.VendedorId;
        entity.MontoPagado = request.MontoPagado;
        entity.EstadoPagadoId = request.EstadoPagadoId;
        entity.FechaEntrega = request.FechaEntrega;
        entity.ComprobantePago = request.ComprobantePago;
        entity.ComprobantePagoNombre = request.ComprobantePagoNombre;
        entity.Observacion = request.Observacion;
        entity.MetodoEntrega = string.IsNullOrWhiteSpace(request.MetodoEntrega) ? "DELIVERY" : request.MetodoEntrega;
        return entity;
    }

    private static string NombreUsuario(Usuario usuario)
    {
        if (usuario.Persona is null)
        {
            return usuario.NombreUsuario ?? $"Usuario {usuario.Id}";
        }

        var nombre = string.Join(" ", new[]
        {
            usuario.Persona.PrimerNombre,
            usuario.Persona.SegundoNombre,
            usuario.Persona.PrimerApellido,
            usuario.Persona.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(nombre) ? usuario.NombreUsuario ?? $"Usuario {usuario.Id}" : nombre;
    }

    public static VentaImpresionDetDto ToDto(this VentaImpresionDet entity)
    {
        return new VentaImpresionDetDto(
            entity.Id,
            entity.CabId,
            entity.ProductoId,
            entity.Producto?.Nombre,
            entity.TipoMaquinaId,
            entity.TipoMaquina?.Nombre,
            entity.Cantidad,
            entity.PrecioUnitario,
            entity.PrecioExtra,
            entity.PrecioTotal,
            entity.ArchivoDisenio,
            entity.ArchivoDisenioNombre,
            entity.Observacion,
            entity.EstadoItem,
            entity.CheckImpresion);
    }

    public static VentaImpresionDet ToEntity(this VentaImpresionDetRequest request, VentaImpresionDet? entity = null)
    {
        entity ??= new VentaImpresionDet();
        entity.CabId = request.CabId;
        entity.ProductoId = request.ProductoId;
        entity.TipoMaquinaId = request.TipoMaquinaId;
        entity.Cantidad = request.Cantidad;
        entity.PrecioUnitario = request.PrecioUnitario;
        entity.PrecioExtra = request.PrecioExtra;
        entity.ArchivoDisenio = request.ArchivoDisenio;
        entity.ArchivoDisenioNombre = request.ArchivoDisenioNombre;
        entity.Observacion = request.Observacion;
        entity.EstadoItem = request.EstadoItem;
        entity.CheckImpresion = request.CheckImpresion;
        return entity;
    }
}
