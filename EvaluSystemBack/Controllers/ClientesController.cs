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
    public async Task<ActionResult<PagedResponse<ClienteDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] int? ciudadId = null,
        [FromQuery] bool? estado = null,
        [FromQuery] int? transportadoraId = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Clientes
            .Include(x => x.Departamento)
            .Include(x => x.Ciudad)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Transportadora)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Departamento)
            .Include(x => x.DatosEnvio)!.ThenInclude(x => x!.Ciudad)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.Nombre != null && x.Nombre.Contains(term)) ||
                (x.Documento != null && x.Documento.Contains(term)) ||
                (x.Email != null && x.Email.Contains(term)) ||
                (x.NroTelefono != null && x.NroTelefono.Contains(term)));
        }

        if (ciudadId.HasValue)
        {
            query = query.Where(x => x.CiudadId == ciudadId.Value || (x.DatosEnvio != null && x.DatosEnvio.CiudadId == ciudadId.Value));
        }

        if (estado.HasValue)
        {
            query = query.Where(x => x.Estado == estado.Value);
        }

        if (transportadoraId.HasValue)
        {
            query = query.Where(x => x.DatosEnvio != null && x.DatosEnvio.TransportadoraId == transportadoraId.Value);
        }

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
            .Include(x => x.Departamento)
            .Include(x => x.Ciudad)
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
        var validationMessage = await ValidateClienteAsync(request);
        if (validationMessage is not null)
        {
            return BadRequest(new { message = validationMessage });
        }

        var cliente = request.ToEntity();
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = cliente.Id }, cliente.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Update(int id, ClienteRequest request)
    {
        var validationMessage = await ValidateClienteAsync(request, id);
        if (validationMessage is not null)
        {
            return BadRequest(new { message = validationMessage });
        }

        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente is null)
        {
            return NotFound();
        }

        request.ToEntity(cliente);
        await _context.SaveChangesAsync();
        return Ok(cliente.ToDto());
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

    private async Task<string?> ValidateClienteAsync(ClienteRequest request, int? currentId = null)
    {
        if (!string.IsNullOrWhiteSpace(request.Documento) && string.IsNullOrWhiteSpace(request.TipoDocumentoId))
        {
            return "Seleccione el tipo de documento.";
        }

        var documento = request.Documento?.Trim();
        var tipoDocumentoId = request.TipoDocumentoId?.Trim();
        var nombre = request.Nombre?.Trim();
        var telefono = request.NroTelefono?.Trim();

        if (!string.IsNullOrWhiteSpace(documento))
        {
            var duplicatedByDocument = await _context.Clientes.AnyAsync(x =>
                (!currentId.HasValue || x.Id != currentId.Value) &&
                x.Documento != null &&
                x.Documento.Trim() == documento &&
                x.TipoDocumentoId == tipoDocumentoId);

            if (duplicatedByDocument)
            {
                return "Ya existe un cliente con ese documento.";
            }
        }
        else if (!string.IsNullOrWhiteSpace(nombre) && !string.IsNullOrWhiteSpace(telefono))
        {
            var duplicatedByNameAndPhone = await _context.Clientes.AnyAsync(x =>
                (!currentId.HasValue || x.Id != currentId.Value) &&
                x.Nombre != null &&
                x.NroTelefono != null &&
                x.Nombre.Trim().ToLower() == nombre.ToLower() &&
                x.NroTelefono.Trim() == telefono);

            if (duplicatedByNameAndPhone)
            {
                return "Ya existe un cliente con ese nombre y telefono.";
            }
        }

        return null;
    }
}
