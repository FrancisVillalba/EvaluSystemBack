using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Options;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EvaluSystemBack.Services;

public class AuthService : IAuthService
{
    private readonly EvaluSystemDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private readonly IConfiguracionService _configuracionService;
    private readonly IPermisoService _permisoService;
    private readonly IPasswordService _passwordService;

    public AuthService(
        EvaluSystemDbContext context,
        IOptions<JwtOptions> jwtOptions,
        IConfiguracionService configuracionService,
        IPermisoService permisoService,
        IPasswordService passwordService)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
        _configuracionService = configuracionService;
        _permisoService = permisoService;
        _passwordService = passwordService;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var usuario = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.NombreUsuario == request.Usuario && x.Estado == true);

        if (usuario is null ||
            string.IsNullOrWhiteSpace(usuario.NombreUsuario) ||
            !_passwordService.VerifyPassword(request.Pass, usuario.PassHash))
        {
            return null;
        }

        var expirationMinutes = await _configuracionService.ObtenerValorIntAsync("MINUTOS_EXPIRACION_SESSION", 1)
            ?? await _configuracionService.ObtenerValorIntAsync("JwtExpirationMinutes", 1)
            ?? _jwtOptions.ExpirationMinutes;
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var persona = BuildPersonaName(usuario.Persona);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, usuario.NombreUsuario),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.NombreUsuario),
            new("usuarioId", usuario.Id.ToString())
        };

        if (usuario.PersonaId.HasValue)
        {
            claims.Add(new Claim("personaId", usuario.PersonaId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(persona))
        {
            claims.Add(new Claim("persona", persona));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenText = new JwtSecurityTokenHandler().WriteToken(token);
        var permisos = await _permisoService.ObtenerPermisosPorUsuarioAsync(usuario.Id);
        return new LoginResponse(tokenText, expiresAt, usuario.Id, usuario.NombreUsuario, usuario.PersonaId, persona, permisos);
    }

    private static string? BuildPersonaName(Models.Persona? persona)
    {
        if (persona is null)
        {
            return null;
        }

        var name = string.Join(" ", new[]
        {
            persona.PrimerNombre,
            persona.SegundoNombre,
            persona.PrimerApellido,
            persona.SegundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
