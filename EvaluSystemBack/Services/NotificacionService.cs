using EvaluSystemBack.Data;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class NotificacionService : INotificacionService
{
    private readonly EvaluSystemDbContext _context;
    public NotificacionService(EvaluSystemDbContext context) => _context = context;

    public async Task AsegurarTablaAsync(CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'[dbo].[Notificaciones]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Notificaciones](
        [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [usuario_id] int NOT NULL,
        [tipo] varchar(30) NOT NULL,
        [titulo] nvarchar(150) NOT NULL,
        [mensaje] nvarchar(500) NOT NULL,
        [pedido_id] int NOT NULL,
        [detalle_id] int NULL,
        [producto] nvarchar(200) NULL,
        [comentario] nvarchar(500) NULL,
        [leida] bit NOT NULL CONSTRAINT [DF_Notificaciones_leida] DEFAULT(0),
        [fecha_creacion] datetime2 NOT NULL,
        [fecha_lectura] datetime2 NULL,
        CONSTRAINT [FK_Notificaciones_Usuarios] FOREIGN KEY([usuario_id]) REFERENCES [dbo].[Usuario]([id])
    );
    CREATE INDEX [IX_Notificaciones_usuario_leida_fecha] ON [dbo].[Notificaciones]([usuario_id], [leida], [fecha_creacion] DESC);
END
""";
        await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task CrearParaUsuarioAsync(int usuarioId, string tipo, string titulo, string mensaje, int pedidoId, int? detalleId, string? producto, string? comentario, CancellationToken cancellationToken)
    {
        await AsegurarTablaAsync(cancellationToken);
        _context.Notificaciones.Add(new Notificacion
        {
            UsuarioId = usuarioId, Tipo = tipo, Titulo = titulo, Mensaje = mensaje,
            PedidoId = pedidoId, DetalleId = detalleId, Producto = producto,
            Comentario = comentario, Leida = false, FechaCreacion = DateTime.Now
        });
    }
}