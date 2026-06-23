using EvaluSystemBack.Dtos;

namespace EvaluSystemBack.Services.Interfaces;

public interface IConfiguracionService
{
    Task<string?> ObtenerValorAsync(string nombre, int nroConfiguracion);
    Task<int?> ObtenerValorIntAsync(string nombre, int nroConfiguracion);
    Task<IEnumerable<ConfiguracionDto>> GetAllAsync();
    Task<ConfiguracionDto?> GetByIdAsync(int id);
    Task<ConfiguracionDto> SaveAsync(ConfiguracionRequest request);
    Task<bool> UpdateAsync(int id, ConfiguracionRequest request);
    Task<bool> DeleteAsync(int id);
}
