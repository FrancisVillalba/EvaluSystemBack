using EvaluSystemBack.Dtos;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfiguracionesController : ControllerBase
{
    private readonly IConfiguracionService _configuracionService;

    public ConfiguracionesController(IConfiguracionService configuracionService)
    {
        _configuracionService = configuracionService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConfiguracionDto>>> GetAll()
    {
        return Ok(await _configuracionService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConfiguracionDto>> GetById(int id)
    {
        var item = await _configuracionService.GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("valor")]
    public async Task<ActionResult<object>> GetValor([FromQuery] string nombre, [FromQuery] int nroConfiguracion)
    {
        var valor = await _configuracionService.ObtenerValorAsync(nombre, nroConfiguracion);
        return valor is null ? NotFound() : Ok(new { nombre, nroConfiguracion, valor });
    }

    [HttpPost]
    public async Task<ActionResult<ConfiguracionDto>> Save(ConfiguracionRequest request)
    {
        var item = await _configuracionService.SaveAsync(request);
        return Ok(item);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ConfiguracionRequest request)
    {
        var updated = await _configuracionService.UpdateAsync(id, request);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _configuracionService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
