using System.Security.Claims;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EvaluSystemBack.Security;

public class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IPermisoService _permisoService;

    private static readonly Dictionary<string, string> ControllerFormularioMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clientes"] = "Clientes",
        ["ClienteDatosEnvio"] = "DatosEnvio",
        ["Transportadoras"] = "Transportadoras",
        ["Productos"] = "Productos",
        ["VentasImpresion"] = "Ventas",
        ["VentasImpresionCab"] = "Ventas",
        ["VentasImpresionDet"] = "Ventas",
        ["Archivos"] = "Ventas",
        ["Personas"] = "Personas",
        ["Usuarios"] = "Usuarios",
        ["Perfiles"] = "Perfiles",
        ["Permisos"] = "Perfiles",
        ["Configuraciones"] = "Configuraciones",
        ["Ciudades"] = "Catalogos",
        ["Departamentos"] = "Catalogos",
        ["EstadosPago"] = "Catalogos",
        ["EstadosVenta"] = "Catalogos",
        ["FormasPago"] = "Catalogos",
        ["TiposCliente"] = "Catalogos",
        ["TiposDocumento"] = "Catalogos",
        ["TiposMaquina"] = "Catalogos"
    };

    public PermissionAuthorizationFilter(IPermisoService permisoService)
    {
        _permisoService = permisoService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (ShouldSkip(context))
        {
            return;
        }

        var usuarioIdValue = context.HttpContext.User.FindFirstValue("usuarioId")
            ?? context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(usuarioIdValue, out var usuarioId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor ||
            !ControllerFormularioMap.TryGetValue(descriptor.ControllerName, out var formulario))
        {
            context.Result = new ForbidResult();
            return;
        }

        var accion = GetAccion(context.HttpContext.Request.Method);
        if (accion is null)
        {
            context.Result = new ForbidResult();
            return;
        }

        var tienePermiso = await _permisoService.UsuarioTienePermisoAsync(usuarioId, formulario, accion);
        if (!tienePermiso)
        {
            context.Result = new ObjectResult(new
            {
                message = $"No tiene permiso para {accion} en {formulario}."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }

    private static bool ShouldSkip(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        return metadata.OfType<IAllowAnonymous>().Any()
            || metadata.OfType<SkipPermissionAttribute>().Any();
    }

    private static string? GetAccion(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => "ver",
            "POST" => "crear",
            "PUT" => "editar",
            "PATCH" => "editar",
            "DELETE" => "eliminar",
            _ => null
        };
    }
}
