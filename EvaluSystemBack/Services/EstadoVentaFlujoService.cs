using EvaluSystemBack.Data;
using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class EstadoVentaFlujoService : IEstadoVentaFlujoService
{
    private readonly EvaluSystemDbContext _context;

    public EstadoVentaFlujoService(EvaluSystemDbContext context)
    {
        _context = context;
    }

    public async Task<EstadoVenta?> ObtenerPorIdAsync(string estadoId, CancellationToken cancellationToken)
    {
        return await _context.EstadosVenta
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == estadoId && x.Estado == "A", cancellationToken);
    }

    public async Task<EstadoVenta?> ObtenerAnteriorAsync(string estadoId, CancellationToken cancellationToken)
    {
        var estado = await ObtenerPorIdAsync(estadoId, cancellationToken);
        if (estado?.NumeroFlujo is null)
        {
            return null;
        }

        return await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => x.Estado == "A" && x.NumeroFlujo < estado.NumeroFlujo)
            .OrderByDescending(x => x.NumeroFlujo)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EstadoVenta?> ObtenerSiguienteAsync(string estadoId, CancellationToken cancellationToken)
    {
        var estado = await ObtenerPorIdAsync(estadoId, cancellationToken);
        return await ObtenerSiguienteAsync(estado, cancellationToken);
    }

    public async Task<EstadoVenta?> ObtenerSiguienteAsync(EstadoVenta? estadoActual, CancellationToken cancellationToken)
    {
        if (estadoActual?.NumeroFlujo is null)
        {
            return null;
        }

        return await _context.EstadosVenta
            .AsNoTracking()
            .Where(x => x.Estado == "A" && x.NumeroFlujo > estadoActual.NumeroFlujo)
            .OrderBy(x => x.NumeroFlujo)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}