using EvaluSystemBack.Models;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Data;

public class EvaluSystemDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public EvaluSystemDbContext(DbContextOptions<EvaluSystemDbContext> options, IHttpContextAccessor? httpContextAccessor = null) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<Ciudad> Ciudades => Set<Ciudad>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ClienteDatosEnvio> ClienteDatosEnvios => Set<ClienteDatosEnvio>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<EstadoPago> EstadosPago => Set<EstadoPago>();
    public DbSet<EstadoVenta> EstadosVenta => Set<EstadoVenta>();
    public DbSet<FormaPago> FormasPago => Set<FormaPago>();
    public DbSet<Formulario> Formularios => Set<Formulario>();
    public DbSet<GrupoVenta> GruposVenta => Set<GrupoVenta>();
    public DbSet<GrupoVentaVendedor> GrupoVentaVendedores => Set<GrupoVentaVendedor>();
    public DbSet<LotePago> LotesPago => Set<LotePago>();
    public DbSet<LotePagoDetalle> LotesPagoDetalle => Set<LotePagoDetalle>();
    public DbSet<MetodoEnvio> MetodosEnvio => Set<MetodoEnvio>();
    public DbSet<Perfil> Perfiles => Set<Perfil>();
    public DbSet<PerfilFormularioPermiso> PerfilFormularioPermisos => Set<PerfilFormularioPermiso>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoComision> ProductoComisiones => Set<ProductoComision>();
    public DbSet<RutaDelivery> RutasDelivery => Set<RutaDelivery>();
    public DbSet<RutaDeliveryDetalle> RutasDeliveryDetalle => Set<RutaDeliveryDetalle>();
    public DbSet<TipoCliente> TiposCliente => Set<TipoCliente>();
    public DbSet<TipoDocumento> TiposDocumento => Set<TipoDocumento>();
    public DbSet<TipoMaquina> TiposMaquina => Set<TipoMaquina>();
    public DbSet<Transportadora> Transportadoras => Set<Transportadora>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioMensajeAceptado> UsuarioMensajesAceptados => Set<UsuarioMensajeAceptado>();
    public DbSet<UsuarioPerfil> UsuarioPerfiles => Set<UsuarioPerfil>();
    public DbSet<VentaImpresionCab> VentasImpresionCab => Set<VentaImpresionCab>();
    public DbSet<VentaImpresionDet> VentasImpresionDet => Set<VentaImpresionDet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ciudad>(entity =>
        {
            entity.ToTable("Ciudad");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DepartamentoId, e.CodigoDistrito }).IsUnique();
            entity.HasIndex(e => new { e.Id, e.DepartamentoId }).IsUnique();
            entity.Property(e => e.CodigoDistrito).HasColumnName("codigoDistrito");
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.HasOne(e => e.Departamento).WithMany(e => e.Ciudades).HasForeignKey(e => e.DepartamentoId);
        });

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.ToTable("Clientes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(150);
            entity.Property(e => e.Documento).HasColumnName("documento").HasMaxLength(50);
            entity.Property(e => e.TipoDocumentoId).HasColumnName("tipo_documento_id").HasMaxLength(50).IsRequired();
            entity.Property(e => e.TipoClienteId).HasColumnName("tipo_cliente").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
            entity.Property(e => e.NroTelefono).HasColumnName("nro_telefono").HasMaxLength(50);
            entity.Property(e => e.Direccion).HasColumnName("direccion").HasMaxLength(200);
            entity.Property(e => e.DepartamentoId).HasColumnName("departamentoId");
            entity.Property(e => e.CiudadId).HasColumnName("ciudadId");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.HasOne(e => e.TipoCliente).WithMany(e => e.Clientes).HasForeignKey(e => e.TipoClienteId);
            entity.HasOne(e => e.TipoDocumento).WithMany(e => e.Clientes).HasForeignKey(e => e.TipoDocumentoId);
            entity.HasOne(e => e.Departamento).WithMany().HasForeignKey(e => e.DepartamentoId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.Ciudad).WithMany().HasForeignKey(e => e.CiudadId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ClienteDatosEnvio>(entity =>
        {
            entity.ToTable("Cliente_datos_envio");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClienteId).IsUnique();
            entity.Property(e => e.ClienteId).HasColumnName("clienteId");
            entity.Property(e => e.TransportadoraId).HasColumnName("transportadoraId");
            entity.Property(e => e.NombreReceptor).HasColumnName("nombre_receptor").HasMaxLength(150).IsRequired();
            entity.Property(e => e.DocumentoReceptor).HasColumnName("documento_receptor").HasMaxLength(50).IsRequired();
            entity.Property(e => e.TelefonoReceptor).HasColumnName("telefono_receptor").HasMaxLength(50).IsRequired();
            entity.Property(e => e.DepartamentoId).HasColumnName("departamentoId");
            entity.Property(e => e.CiudadId).HasColumnName("ciudadId");
            entity.Property(e => e.Direccion).HasColumnName("direccion").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Observacion).HasColumnName("observacion").HasMaxLength(500);
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.Cliente).WithOne(e => e.DatosEnvio).HasForeignKey<ClienteDatosEnvio>(e => e.ClienteId);
            entity.HasOne(e => e.Transportadora).WithMany(e => e.ClienteDatosEnvios).HasForeignKey(e => e.TransportadoraId);
            entity.HasOne(e => e.Departamento).WithMany(e => e.ClienteDatosEnvios).HasForeignKey(e => e.DepartamentoId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.Ciudad).WithMany(e => e.ClienteDatosEnvios)
                .HasForeignKey(e => new { e.CiudadId, e.DepartamentoId })
                .HasPrincipalKey(e => new { e.Id, e.DepartamentoId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Configuracion>(entity =>
        {
            entity.ToTable("Configuraciones");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Nombre, e.NroConfiguracion }).IsUnique();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.NroConfiguracion).HasColumnName("nroConfiguracion");
            entity.Property(e => e.Valor).HasColumnName("valor").HasColumnType("varchar(max)").IsRequired();
        });

        modelBuilder.Entity<Departamento>(entity =>
        {
            entity.ToTable("Departamento");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado");
        });

        modelBuilder.Entity<EstadoPago>(entity =>
        {
            entity.ToTable("Estado_pago");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50);
            entity.Property(e => e.Estado).HasColumnName("estado");
        });

        modelBuilder.Entity<EstadoVenta>(entity =>
        {
            entity.ToTable("Estados_venta");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(2).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50);
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(50);
            entity.Property(e => e.NumeroFlujo).HasColumnName("numero_flujo");
        });

        modelBuilder.Entity<FormaPago>(entity =>
        {
            entity.ToTable("Forma_pago");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(1).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50);
            entity.Property(e => e.Estado).HasColumnName("estado");
        });

        modelBuilder.Entity<Formulario>(entity =>
        {
            entity.ToTable("Formularios");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Nombre).IsUnique();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(255);
            entity.Property(e => e.Ruta).HasColumnName("ruta").HasMaxLength(200);
            entity.Property(e => e.Icono).HasColumnName("icono").HasMaxLength(100);
            entity.Property(e => e.Orden).HasColumnName("orden");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FormularioPadreId).HasColumnName("formulario_padre_id");
            entity.HasOne(e => e.FormularioPadre).WithMany(e => e.FormulariosHijos).HasForeignKey(e => e.FormularioPadreId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GrupoVenta>(entity =>
        {
            entity.ToTable("Grupo_venta");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
            entity.Property(e => e.TeamLeaderUsuarioId).HasColumnName("team_leader_usuario_id");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.TeamLeaderUsuario).WithMany().HasForeignKey(e => e.TeamLeaderUsuarioId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GrupoVentaVendedor>(entity =>
        {
            entity.ToTable("Grupo_venta_vendedor");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GrupoVentaId, e.VendedorUsuarioId }).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.GrupoVentaId).HasColumnName("grupo_venta_id");
            entity.Property(e => e.VendedorUsuarioId).HasColumnName("vendedor_usuario_id");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.GrupoVenta).WithMany(e => e.Vendedores).HasForeignKey(e => e.GrupoVentaId);
            entity.HasOne(e => e.VendedorUsuario).WithMany().HasForeignKey(e => e.VendedorUsuarioId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LotePago>(entity =>
        {
            entity.ToTable("Lote_pago");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TipoPago).HasColumnName("tipo_pago").HasMaxLength(50).IsRequired();
            entity.Property(e => e.FechaGeneracion).HasColumnName("fecha_generacion").HasColumnType("datetime");
            entity.Property(e => e.UsuarioGeneroId).HasColumnName("usuario_genero_id");
            entity.Property(e => e.FechaDesde).HasColumnName("fecha_desde").HasColumnType("date");
            entity.Property(e => e.FechaHasta).HasColumnName("fecha_hasta").HasColumnType("date");
            entity.Property(e => e.FechaPago).HasColumnName("fecha_pago").HasColumnType("date");
            entity.Property(e => e.VendedorId).HasColumnName("vendedor_id");
            entity.Property(e => e.MontoTotal).HasColumnName("monto_total").HasPrecision(18, 2);
            entity.Property(e => e.CantidadPersonas).HasColumnName("cantidad_personas");
            entity.Property(e => e.NombreArchivo).HasColumnName("nombre_archivo").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(30).IsRequired();
            entity.Property(e => e.ContenidoTxt).HasColumnName("contenido_txt").HasColumnType("varchar(max)").IsRequired();
            entity.HasOne(e => e.UsuarioGenero).WithMany().HasForeignKey(e => e.UsuarioGeneroId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.Vendedor).WithMany().HasForeignKey(e => e.VendedorId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LotePagoDetalle>(entity =>
        {
            entity.ToTable("Lote_pago_detalle");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LotePagoId).HasColumnName("lote_pago_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Vendedor).HasColumnName("vendedor").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Documento).HasColumnName("documento").HasMaxLength(50).IsRequired();
            entity.Property(e => e.CuentaDebitoEmpresa).HasColumnName("cuenta_debito_empresa").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Concepto).HasColumnName("concepto").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Monto).HasColumnName("monto").HasPrecision(18, 2);
            entity.Property(e => e.EsAguinaldo).HasColumnName("es_aguinaldo").HasMaxLength(2).IsRequired();
            entity.Property(e => e.FechaPago).HasColumnName("fecha_pago").HasColumnType("date");
            entity.Property(e => e.TipoCuenta).HasColumnName("tipo_cuenta").HasMaxLength(3).IsRequired();
            entity.Property(e => e.LineaTxt).HasColumnName("linea_txt").HasMaxLength(1000).IsRequired();
            entity.HasOne(e => e.LotePago).WithMany(e => e.Detalles).HasForeignKey(e => e.LotePagoId);
            entity.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<MetodoEnvio>(entity =>
        {
            entity.ToTable("Metodo_envio");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(30).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado");
        });

        modelBuilder.Entity<Perfil>(entity =>
        {
            entity.ToTable("Perfiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(255);
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
        });

        modelBuilder.Entity<PerfilFormularioPermiso>(entity =>
        {
            entity.ToTable("Perfil_formulario_permiso");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PerfilId, e.FormularioId }).IsUnique();
            entity.Property(e => e.PerfilId).HasColumnName("perfilId");
            entity.Property(e => e.FormularioId).HasColumnName("formularioId");
            entity.Property(e => e.PuedeVer).HasColumnName("puedeVer");
            entity.Property(e => e.PuedeCrear).HasColumnName("puedeCrear");
            entity.Property(e => e.PuedeEditar).HasColumnName("puedeEditar");
            entity.Property(e => e.PuedeEliminar).HasColumnName("puedeEliminar");
            entity.HasOne(e => e.Perfil).WithMany(e => e.FormularioPermisos).HasForeignKey(e => e.PerfilId);
            entity.HasOne(e => e.Formulario).WithMany(e => e.PerfilPermisos).HasForeignKey(e => e.FormularioId);
        });

        modelBuilder.Entity<Persona>(entity =>
        {
            entity.ToTable("Persona");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PrimerNombre).HasColumnName("primer_nombre").HasMaxLength(50);
            entity.Property(e => e.SegundoNombre).HasColumnName("segundo_nombre").HasMaxLength(50);
            entity.Property(e => e.PrimerApellido).HasColumnName("primer_apellido").HasMaxLength(50);
            entity.Property(e => e.SegundoApellido).HasColumnName("segundo_apellido").HasMaxLength(50);
            entity.Property(e => e.FechaCumpleanios).HasColumnName("fecha_cumpleaños").HasColumnType("datetime");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.Property(e => e.TipoDocumentoId).HasColumnName("tipo_documento").HasMaxLength(50);
            entity.Property(e => e.Documento).HasColumnName("documento").HasMaxLength(50);
            entity.Property(e => e.Telefono).HasColumnName("telefono").HasMaxLength(50);
            entity.HasOne(e => e.TipoDocumento).WithMany(e => e.Personas).HasForeignKey(e => e.TipoDocumentoId);
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.ToTable("Productos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
            entity.Property(e => e.PrecioBase).HasColumnName("precio_base").HasPrecision(18, 2);
            entity.Property(e => e.MaquinaId).HasColumnName("maquinaId");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.HasOne(e => e.TipoMaquina).WithMany(e => e.Productos).HasForeignKey(e => e.MaquinaId);
        });

        modelBuilder.Entity<ProductoComision>(entity =>
        {
            entity.ToTable("Producto_comision");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProductoId, e.PerfilId, e.Estado });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProductoId).HasColumnName("productoId");
            entity.Property(e => e.PerfilId).HasColumnName("perfilId");
            entity.Property(e => e.MontoPorMetro).HasColumnName("monto_por_metro").HasPrecision(18, 2);
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaDesde).HasColumnName("fecha_desde").HasColumnType("date");
            entity.Property(e => e.FechaHasta).HasColumnName("fecha_hasta").HasColumnType("date");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.Producto).WithMany(e => e.Comisiones).HasForeignKey(e => e.ProductoId);
            entity.HasOne(e => e.Perfil).WithMany(e => e.ProductoComisiones).HasForeignKey(e => e.PerfilId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RutaDelivery>(entity =>
        {
            entity.ToTable("Ruta_delivery");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NumeroLote).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NumeroLote).HasColumnName("numero_lote").HasMaxLength(30).IsRequired();
            entity.Property(e => e.UsuarioDeliveryId).HasColumnName("usuario_delivery_id");
            entity.Property(e => e.FechaGeneracion).HasColumnName("fecha_generacion").HasColumnType("datetime");
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(30).IsRequired();
            entity.Property(e => e.Observacion).HasColumnName("observacion").HasMaxLength(500);
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.UsuarioDelivery).WithMany().HasForeignKey(e => e.UsuarioDeliveryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RutaDeliveryDetalle>(entity =>
        {
            entity.ToTable("Ruta_delivery_detalle");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VentaId).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RutaDeliveryId).HasColumnName("ruta_delivery_id");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");
            entity.Property(e => e.FechaAgregado).HasColumnName("fecha_agregado").HasColumnType("datetime");
            entity.HasOne(e => e.RutaDelivery).WithMany(e => e.Detalles).HasForeignKey(e => e.RutaDeliveryId);
            entity.HasOne(e => e.Venta).WithMany().HasForeignKey(e => e.VentaId).OnDelete(DeleteBehavior.NoAction);
        });

        ConfigureSimpleCatalog<TipoCliente>(modelBuilder, "Tipo_cliente");
        ConfigureSimpleCatalog<TipoDocumento>(modelBuilder, "Tipo_documento");

        modelBuilder.Entity<TipoMaquina>(entity =>
        {
            entity.ToTable("Tipo_maquina");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion").IsRequired();
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
        });

        modelBuilder.Entity<Transportadora>(entity =>
        {
            entity.ToTable("Transportadoras");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
            entity.Property(e => e.Telefono).HasColumnName("telefono").HasMaxLength(50);
            entity.Property(e => e.Direccion).HasColumnName("direccion").HasMaxLength(200);
            entity.Property(e => e.Observacion).HasColumnName("observacion").HasMaxLength(500);
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion").IsRequired();
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("Usuario");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NombreUsuario).HasColumnName("usuario").HasMaxLength(50);
            entity.Property(e => e.PassHash).HasColumnName("pass_hash").HasMaxLength(512);
            entity.Property(e => e.PersonaId).HasColumnName("perId");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.Persona).WithMany(e => e.Usuarios).HasForeignKey(e => e.PersonaId);
        });

        modelBuilder.Entity<UsuarioMensajeAceptado>(entity =>
        {
            entity.ToTable("Usuario_mensaje_aceptado");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UsuarioId, e.Clave }).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Clave).HasColumnName("clave").HasMaxLength(200).IsRequired();
            entity.Property(e => e.FechaAceptado).HasColumnName("fecha_aceptado").HasColumnType("datetime");
            entity.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsuarioPerfil>(entity =>
        {
            entity.ToTable("Usuario_perfil");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UsuarioId, e.PerfilId }).IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.PerfilId).HasColumnName("perfil_id");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.HasOne(e => e.Usuario).WithMany(e => e.Perfiles).HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Perfil).WithMany(e => e.Usuarios).HasForeignKey(e => e.PerfilId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VentaImpresionCab>(entity =>
        {
            entity.ToTable("Ventas_impresion_cab");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClienteId).HasColumnName("clienteId");
            entity.Property(e => e.FormaPagoId).HasColumnName("formaPagoId").HasMaxLength(1).IsRequired();
            entity.Property(e => e.TotalVenta).HasColumnName("total_venta").HasPrecision(18, 2);
            entity.Property(e => e.MontoEnvioTransportadora).HasColumnName("monto_envio_transportadora").HasPrecision(18, 2);
            entity.Property(e => e.EstadoVentaId).HasColumnName("estadoVentaId").HasMaxLength(2).IsRequired();
            entity.Property(e => e.VendedorId).HasColumnName("vendedorId");
            entity.Property(e => e.MontoPagado).HasColumnName("montoPagado").HasPrecision(18, 2);
            entity.Property(e => e.EstadoPagadoId).HasColumnName("estadoPagado").HasMaxLength(50);
            entity.Property(e => e.FechaEntrega).HasColumnName("fecha_entrega").HasColumnType("datetime");
            entity.Property(e => e.ComprobantePago).HasColumnName("comprobante_pago").HasMaxLength(500);
            entity.Property(e => e.ComprobantePagoNombre).HasColumnName("comprobante_pago_nombre").HasMaxLength(255);
            entity.Property(e => e.Observacion).HasColumnName("observacion").HasMaxLength(500);
            entity.Property(e => e.MetodoEntrega).HasColumnName("metodo_entrega").HasMaxLength(30).HasDefaultValue("DELIVERY");
            entity.Property(e => e.Reposicion).HasColumnName("reposicion").HasDefaultValue(false);
            entity.Property(e => e.UsuarioEntregaPedidoId).HasColumnName("usuario_entrega_pedido_id");
            entity.Property(e => e.FechaTomaDelivery).HasColumnName("fecha_toma_delivery").HasColumnType("datetime");
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.Cliente).WithMany(e => e.Ventas).HasForeignKey(e => e.ClienteId);
            entity.HasOne(e => e.FormaPago).WithMany(e => e.Ventas).HasForeignKey(e => e.FormaPagoId);
            entity.HasOne(e => e.EstadoPago).WithMany(e => e.Ventas).HasForeignKey(e => e.EstadoPagadoId);
            entity.HasOne(e => e.EstadoVenta).WithMany(e => e.Ventas).HasForeignKey(e => e.EstadoVentaId);
            entity.HasOne(e => e.MetodoEnvio).WithMany(e => e.Ventas).HasForeignKey(e => e.MetodoEntrega).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.UsuarioEntregaPedido).WithMany().HasForeignKey(e => e.UsuarioEntregaPedidoId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VentaImpresionDet>(entity =>
        {
            entity.ToTable("Ventas_impresion_det");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CabId).HasColumnName("cabId");
            entity.Property(e => e.ProductoId).HasColumnName("productoId");
            entity.Property(e => e.TipoMaquinaId).HasColumnName("tipoMaquinaId");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad").HasPrecision(18, 2);
            entity.Property(e => e.PrecioUnitario).HasColumnName("precio_unitario").HasPrecision(18, 2);
            entity.Property(e => e.PrecioExtra).HasColumnName("precio_extra").HasPrecision(18, 2);
            entity.Property(e => e.PrecioTotal).HasColumnName("precio_total").HasPrecision(38, 4).HasComputedColumnSql("([cantidad]*[precio_unitario]+[precio_extra])", stored: false);
            entity.Property(e => e.ArchivoDisenio).HasColumnName("archivo_diseño").HasMaxLength(500);
            entity.Property(e => e.ArchivoDisenioNombre).HasColumnName("archivo_diseño_nombre").HasMaxLength(255);
            entity.Property(e => e.Observacion).HasColumnName("observacion").HasMaxLength(500);
            entity.Property(e => e.EstadoItem).HasColumnName("estado_item").HasMaxLength(2).IsFixedLength().IsRequired();
            entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion").HasColumnType("datetime");
            entity.Property(e => e.CheckImpresion).HasColumnName("checkImpresion");
            entity.Property(e => e.UsuCreacion).HasColumnName("usu_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion").HasColumnType("datetime");
            entity.Property(e => e.UsuModificacion).HasColumnName("usu_modificacion");
            entity.HasOne(e => e.Cabecera).WithMany(e => e.Detalles).HasForeignKey(e => e.CabId);
            entity.HasOne(e => e.Producto).WithMany(e => e.VentasDetalle).HasForeignKey(e => e.ProductoId);
            entity.HasOne(e => e.TipoMaquina).WithMany(e => e.VentasDetalle).HasForeignKey(e => e.TipoMaquinaId);
        });
    }

    private static void ConfigureSimpleCatalog<T>(ModelBuilder modelBuilder, string tableName) where T : SimpleStringCatalog
    {
        modelBuilder.Entity<T>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50).ValueGeneratedNever();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50);
            entity.Property(e => e.Estado).HasColumnName("estado");
        });
    }

    public override int SaveChanges()
    {
        ApplyAuditValues();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditValues();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditValues()
    {
        var now = DateTime.Now;
        var userId = GetCurrentUserId();

        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added)
            {
                SetDateValue(entry, "FechaCreacion", now, onlyIfEmpty: true);
                SetUserValue(entry, "UsuCreacion", userId, onlyIfEmpty: true);
                SetDateValue(entry, "FechaModificacion", now, onlyIfEmpty: true);
                SetUserValue(entry, "UsuModificacion", userId, onlyIfEmpty: true);
            }

            if (entry.State == EntityState.Modified)
            {
                SetDateValue(entry, "FechaModificacion", now, onlyIfEmpty: false);
                SetUserValue(entry, "UsuModificacion", userId, onlyIfEmpty: false);
            }
        }
    }

    private int? GetCurrentUserId()
    {
        var value = _httpContextAccessor?.HttpContext?.User.FindFirst("usuarioId")?.Value
            ?? _httpContextAccessor?.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(value, out var userId) ? userId : null;
    }

    private static void SetDateValue(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string propertyName, DateTime now, bool onlyIfEmpty)
    {
        var property = entry.Metadata.FindProperty(propertyName);
        if (property is null)
        {
            return;
        }

        var currentValue = entry.Property(propertyName).CurrentValue;
        if (onlyIfEmpty && currentValue is not null && !Equals(currentValue, GetDefaultValue(property.ClrType)))
        {
            return;
        }

        if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
        {
            entry.Property(propertyName).CurrentValue = now;
        }
        else if (property.ClrType == typeof(DateOnly) || property.ClrType == typeof(DateOnly?))
        {
            entry.Property(propertyName).CurrentValue = DateOnly.FromDateTime(now);
        }
    }

    private static void SetUserValue(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string propertyName, int? userId, bool onlyIfEmpty)
    {
        var property = entry.Metadata.FindProperty(propertyName);
        if (property is null)
        {
            return;
        }

        var currentValue = entry.Property(propertyName).CurrentValue;
        if (onlyIfEmpty && currentValue is not null && !Equals(currentValue, GetDefaultValue(property.ClrType)))
        {
            return;
        }

        if (property.ClrType == typeof(int) || property.ClrType == typeof(int?))
        {
            entry.Property(propertyName).CurrentValue = userId ?? (property.IsNullable ? null : 1);
        }
    }

    private static object? GetDefaultValue(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        return underlyingType is null && type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
