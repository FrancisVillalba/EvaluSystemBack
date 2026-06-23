using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CiudadesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public CiudadesController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CiudadDto>>> GetAll([FromQuery] int? departamentoId)
    {
        var query = _context.Ciudades.Include(x => x.Departamento).AsNoTracking();
        if (departamentoId.HasValue)
        {
            query = query.Where(x => x.DepartamentoId == departamentoId.Value);
        }

        var items = await query.OrderBy(x => x.DepartamentoId).ThenBy(x => x.Nombre).ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CiudadDto>> GetById(int id)
    {
        var item = await _context.Ciudades.Include(x => x.Departamento).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }
}
