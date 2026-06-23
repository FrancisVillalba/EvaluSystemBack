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
    public async Task<ActionResult<IEnumerable<ClienteDto>>> GetAll()
    {
        var clientes = await _context.Clientes
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Transportadora)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .AsNoTracking()
            .ToListAsync();

        return Ok(clientes.Select(x => x.ToDto()));
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
