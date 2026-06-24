IF SCHEMA_ID(N'catalogos') IS NULL EXEC(N'CREATE SCHEMA catalogos');
IF SCHEMA_ID(N'clientes') IS NULL EXEC(N'CREATE SCHEMA clientes');
IF SCHEMA_ID(N'config') IS NULL EXEC(N'CREATE SCHEMA config');
IF SCHEMA_ID(N'seguridad') IS NULL EXEC(N'CREATE SCHEMA seguridad');
IF SCHEMA_ID(N'ventas') IS NULL EXEC(N'CREATE SCHEMA ventas');
GO

IF OBJECT_ID(N'dbo.Ciudad', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Ciudad;
IF OBJECT_ID(N'dbo.Departamento', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Departamento;
IF OBJECT_ID(N'dbo.Productos', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Productos;
IF OBJECT_ID(N'dbo.Tipo_cliente', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Tipo_cliente;
IF OBJECT_ID(N'dbo.Tipo_documento', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Tipo_documento;
IF OBJECT_ID(N'dbo.Tipo_maquina', N'U') IS NOT NULL ALTER SCHEMA catalogos TRANSFER dbo.Tipo_maquina;
GO

IF OBJECT_ID(N'dbo.Clientes', N'U') IS NOT NULL ALTER SCHEMA clientes TRANSFER dbo.Clientes;
IF OBJECT_ID(N'dbo.Cliente_datos_envio', N'U') IS NOT NULL ALTER SCHEMA clientes TRANSFER dbo.Cliente_datos_envio;
IF OBJECT_ID(N'dbo.Transportadoras', N'U') IS NOT NULL ALTER SCHEMA clientes TRANSFER dbo.Transportadoras;
GO

IF OBJECT_ID(N'dbo.Configuraciones', N'U') IS NOT NULL ALTER SCHEMA config TRANSFER dbo.Configuraciones;
GO

IF OBJECT_ID(N'dbo.Formularios', N'U') IS NOT NULL ALTER SCHEMA seguridad TRANSFER dbo.Formularios;
IF OBJECT_ID(N'dbo.Perfiles', N'U') IS NOT NULL ALTER SCHEMA seguridad TRANSFER dbo.Perfiles;
IF OBJECT_ID(N'dbo.Perfil_formulario_permiso', N'U') IS NOT NULL ALTER SCHEMA seguridad TRANSFER dbo.Perfil_formulario_permiso;
IF OBJECT_ID(N'dbo.Persona', N'U') IS NOT NULL ALTER SCHEMA seguridad TRANSFER dbo.Persona;
IF OBJECT_ID(N'dbo.Usuario', N'U') IS NOT NULL ALTER SCHEMA seguridad TRANSFER dbo.Usuario;
GO

IF OBJECT_ID(N'dbo.Estado_pago', N'U') IS NOT NULL ALTER SCHEMA ventas TRANSFER dbo.Estado_pago;
IF OBJECT_ID(N'dbo.Estados_venta', N'U') IS NOT NULL ALTER SCHEMA ventas TRANSFER dbo.Estados_venta;
IF OBJECT_ID(N'dbo.Forma_pago', N'U') IS NOT NULL ALTER SCHEMA ventas TRANSFER dbo.Forma_pago;
IF OBJECT_ID(N'dbo.Ventas_impresion_cab', N'U') IS NOT NULL ALTER SCHEMA ventas TRANSFER dbo.Ventas_impresion_cab;
IF OBJECT_ID(N'dbo.Ventas_impresion_det', N'U') IS NOT NULL ALTER SCHEMA ventas TRANSFER dbo.Ventas_impresion_det;
GO
