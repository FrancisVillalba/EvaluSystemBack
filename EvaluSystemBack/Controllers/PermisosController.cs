using System.Security.Claims;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Security;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PermisosController : ControllerBase
{
    private readonly IPermisoService _permisoService;

    public PermisosController(IPermisoService permisoService)
    {
        _permisoService = permisoService;
    }

    [HttpGet("mis-permisos")]
    [SkipPermission]
    public async Task<ActionResult<IEnumerable<PerfilFormularioPermisoDto>>> MisPermisos()
    {
        var usuarioIdValue = User.FindFirstValue("usuarioId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(usuarioIdValue, out var usuarioId))
        {
            return Unauthorized();
        }

        var permisos = await _permisoService.ObtenerPermisosPorUsuarioAsync(usuarioId);
        return Ok(permisos);
    }

    [HttpGet("usuario/{usuarioId:int}")]
    public async Task<ActionResult<IEnumerable<PerfilFormularioPermisoDto>>> GetByUsuario(int usuarioId)
    {
        var permisos = await _permisoService.ObtenerPermisosPorUsuarioAsync(usuarioId);
        return Ok(permisos);
    }

    [HttpGet("perfil/{perfilId:int}")]
    public async Task<ActionResult<IEnumerable<PerfilFormularioPermisoDto>>> GetByPerfil(int perfilId)
    {
        var permisos = await _permisoService.ObtenerPermisosPorPerfilAsync(perfilId);
        return Ok(permisos);
    }

    [HttpGet("formularios")]
    public async Task<ActionResult<IEnumerable<FormularioDto>>> GetFormularios()
    {
        var formularios = await _permisoService.GetFormulariosAsync();
        return Ok(formularios);
    }

    [HttpPost("formularios")]
    public async Task<ActionResult<FormularioDto>> SaveFormulario(FormularioRequest request)
    {
        var formulario = await _permisoService.SaveFormularioAsync(request);
        return Ok(formulario);
    }

    [HttpPost("perfil-formulario")]
    public async Task<ActionResult<PerfilFormularioPermisoDto>> SavePermiso(PerfilFormularioPermisoRequest request)
    {
        var permiso = await _permisoService.SavePermisoAsync(request);
        return Ok(permiso);
    }
}
