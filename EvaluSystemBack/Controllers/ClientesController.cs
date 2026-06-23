using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public ClientesController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ClienteDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Clientes
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Transportadora)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .AsNoTracking();

        var totalItems = await query.CountAsync();
        var clientes = await query
            .OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResponse<ClienteDto>(
            clientes.Select(x => x.ToDto()),
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDto>> GetById(int id)
    {
        var cliente = await _context.Clientes
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Transportadora)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        return cliente is null ? NotFound() : Ok(cliente.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ClienteDto>> Create(ClienteRequest request)
    {
        var cliente = request.ToEntity();
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = cliente.Id }, cliente.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ClienteRequest request)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente is null)
        {
            return NotFound();
        }

        request.ToEntity(cliente);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente is null)
        {
            return NotFound();
        }

        _context.Clientes.Remove(cliente);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
