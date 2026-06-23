using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Mapping;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsuariosController : ControllerBase
{
    private readonly EvaluSystemDbContext _context;
    private readonly IPasswordService _passwordService;

    public UsuariosController(EvaluSystemDbContext context, IPasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UsuarioDto>>> GetAll()
    {
        var items = await _context.Usuarios.Include(x => x.Persona).AsNoTracking().ToListAsync();
        return Ok(items.Select(x => x.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UsuarioDto>> GetById(int id)
    {
        var item = await _context.Usuarios.Include(x => x.Persona).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<UsuarioDto>> Create(UsuarioRequest request)
    {
        var item = request.ToEntity();
        if (!string.IsNullOrWhiteSpace(request.Pass))
        {
            item.PassHash = _passwordService.HashPassword(request.Pass);
        }

        _context.Usuarios.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UsuarioRequest request)
    {
        var item = await _context.Usuarios.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        request.ToEntity(item);
        if (!string.IsNullOrWhiteSpace(request.Pass))
        {
            item.PassHash = _passwordService.HashPassword(request.Pass);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Usuarios.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        _context.Usuarios.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
