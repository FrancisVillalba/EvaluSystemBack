using EvaluSystemBack.Dtos;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        return response is null ? Unauthorized(new { message = "Usuario o contraseña incorrectos." }) : Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshTokenRequest request)
    {
        var response = await _authService.RefreshAsync(request);
        return response is null
            ? Unauthorized(new { message = "La sesión caducó. Inicie sesión nuevamente." })
            : Ok(response);
    }
}
