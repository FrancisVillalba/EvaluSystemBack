using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Services;

public class ConfiguracionService : IConfiguracionService
{
    private readonly EvaluSystemDbContext _context;

    public ConfiguracionService(EvaluSystemDbContext context)
    {
        _context = context;
    }

    public async Task<string?> ObtenerValorAsync(string nombre, int nroConfiguracion)
    {
        return await _context.Configuraciones
            .AsNoTracking()
            .Where(x => x.Nombre == nombre && x.NroConfiguracion == nroConfiguracion)
            .Select(x => x.Valor)
            .FirstOrDefaultAsync();
    }

    public async Task<int?> ObtenerValorIntAsync(string nombre, int nroConfiguracion)
    {
        var valor = await ObtenerValorAsync(nombre, nroConfiguracion);
        return int.TryParse(valor, out var result) ? result : null;
    }

    public async Task<IEnumerable<ConfiguracionDto>> GetAllAsync()
    {
        var items = await _context.Configuraciones.AsNoTracking().OrderBy(x => x.Nombre).ThenBy(x => x.NroConfiguracion).ToListAsync();
        return items.Select(x => x.ToDto());
    }

    public async Task<ConfiguracionDto?> GetByIdAsync(int id)
    {
        var item = await _context.Configuraciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item?.ToDto();
    }

    public async Task<ConfiguracionDto> SaveAsync(ConfiguracionRequest request)
    {
        var item = await _context.Configuraciones
            .FirstOrDefaultAsync(x => x.Nombre == request.Nombre && x.NroConfiguracion == request.NroConfiguracion);

        if (item is null)
        {
            item = request.ToEntity();
            _context.Configuraciones.Add(item);
        }
        else
        {
            request.ToEntity(item);
        }

        await _context.SaveChangesAsync();
        return item.ToDto();
    }

    public async Task<bool> UpdateAsync(int id, ConfiguracionRequest request)
    {
        var item = await _context.Configuraciones.FindAsync(id);
        if (item is null)
        {
            return false;
        }

        request.ToEntity(item);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.Configuraciones.FindAsync(id);
        if (item is null)
        {
            return false;
        }

        _context.Configuraciones.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
