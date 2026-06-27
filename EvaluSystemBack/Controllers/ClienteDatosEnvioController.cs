using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClienteDatosEnvioController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;

    public ClienteDatosEnvioController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClienteDatosEnvioDto>>> GetAll()
    {
        var envios = await Query().AsNoTracking().ToListAsync();
        return Ok(envios.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDatosEnvioDto>> GetById(int id)
    {
        var envio = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return envio is null ? NotFound() : Ok(envio.ToDto());
    }

    [HttpGet("cliente/{clienteId:int}")]
    public async Task<ActionResult<ClienteDatosEnvioDto>> GetByCliente(int clienteId)
    {
        var envio = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.ClienteId == clienteId);
        return envio is null ? NotFound() : Ok(envio.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ClienteDatosEnvioDto>> Create(ClienteDatosEnvioRequest request)
    {
        var envio = request.ToEntity();
        _context.ClienteDatosEnvios.Add(envio);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = envio.Id }, envio.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClienteDatosEnvioDto>> Update(int id, ClienteDatosEnvioRequest request)
    {
        var envio = await _context.ClienteDatosEnvios.FindAsync(id);
        if (envio is null)
        {
            return NotFound();
        }

        request.ToEntity(envio);
        await _context.SaveChangesAsync();
        return Ok(envio.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var envio = await _context.ClienteDatosEnvios.FindAsync(id);
        if (envio is null)
        {
            return NotFound();
        }

        _context.ClienteDatosEnvios.Remove(envio);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Models.ClienteDatosEnvio> Query()
    {
        return _context.ClienteDatosEnvios
            .Include(x => x.Transportadora)
            .Include(x => x.Departamento)
            .Include(x => x.Ciudad);
    }
}
