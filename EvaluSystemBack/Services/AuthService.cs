using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using EvaluSystemBack.Options;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EvaluSystemBack.Services;

public class AuthService : IAuthService
{
    private const string RefreshTokenType = "refresh";
    private readonly EvaluSystemDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private readonly IPermisoService _permisoService;
    private readonly IPasswordService _passwordService;

    public AuthService(
        EvaluSystemDbContext context,
        IOptions<JwtOptions> jwtOptions,
        IPermisoService permisoService,
        IPasswordService passwordService)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
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

        return await CreateLoginResponseAsync(usuario);
    }

    public async Task<LoginResponse?> RefreshAsync(RefreshTokenRequest request)
    {
        var refreshTokenData = ValidateRefreshToken(request.RefreshToken);
        if (!refreshTokenData.HasValue)
        {
            return null;
        }

        var usuario = await _context.Usuarios
            .Include(x => x.Persona)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == refreshTokenData.Value.UserId && x.Estado == true);

        return usuario is null || string.IsNullOrWhiteSpace(usuario.NombreUsuario)
            ? null
            : await CreateLoginResponseAsync(usuario, request.RefreshToken, refreshTokenData.Value.ExpiresAt);
    }

    private async Task<LoginResponse> CreateLoginResponseAsync(Usuario usuario, string? currentRefreshToken = null, DateTime? currentRefreshExpiresAt = null)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_jwtOptions.ExpirationMinutes);
        var refreshExpiresAt = currentRefreshExpiresAt ?? now.AddHours(_jwtOptions.RefreshExpirationHours);
        var persona = BuildPersonaName(usuario.Persona);
        var accessToken = WriteToken(BuildAccessClaims(usuario, persona), expiresAt);
        var refreshToken = currentRefreshToken ?? WriteToken(new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new("token_type", RefreshTokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        }, refreshExpiresAt);
        var permisos = await _permisoService.ObtenerPermisosPorUsuarioAsync(usuario.Id);

        return new LoginResponse(
            accessToken,
            expiresAt,
            refreshToken,
            refreshExpiresAt,
            usuario.Id,
            usuario.NombreUsuario!,
            usuario.PersonaId,
            persona,
            permisos);
    }

    private static List<Claim> BuildAccessClaims(Usuario usuario, string? persona)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, usuario.NombreUsuario!),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.NombreUsuario!),
            new("usuarioId", usuario.Id.ToString()),
            new("token_type", "access")
        };

        if (usuario.PersonaId.HasValue)
        {
            claims.Add(new Claim("personaId", usuario.PersonaId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(persona))
        {
            claims.Add(new Claim("persona", persona));
        }

        return claims;
    }

    private string WriteToken(IEnumerable<Claim> claims, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (int UserId, DateTime ExpiresAt)? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwtOptions.Issuer,
                ValidAudience = _jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key)),
                ClockSkew = TimeSpan.Zero
            }, out var validatedToken);

            if (principal.FindFirstValue("token_type") != RefreshTokenType)
            {
                return null;
            }

            var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(userIdValue, out var userId)
                ? (userId, validatedToken.ValidTo)
                : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private static string? BuildPersonaName(Persona? persona)
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
