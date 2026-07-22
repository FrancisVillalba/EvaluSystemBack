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
        ["ProductoComisiones"] = "Productos",
        ["GruposVenta"] = "Grupo de ventas",
        ["VentasImpresion"] = "Pedidos",
        ["Impresiones"] = "Impresiones",
        ["Delivery"] = "Envio",
        ["Control"] = "Control",
        ["Reportes"] = "Reportes",
        ["VentasImpresionCab"] = "Pedidos",
        ["VentasImpresionDet"] = "Pedidos",
        ["Archivos"] = "Pedidos",
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

        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            context.Result = new ForbidResult();
            return;
        }

        var formulario = ResolveFormulario(descriptor, context.HttpContext.Request);
        if (string.IsNullOrWhiteSpace(formulario))
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

    private static string? ResolveFormulario(ControllerActionDescriptor descriptor, HttpRequest request)
    {
        if (!descriptor.ControllerName.Equals("Reportes", StringComparison.OrdinalIgnoreCase))
        {
            return ControllerFormularioMap.TryGetValue(descriptor.ControllerName, out var formulario)
                ? formulario
                : null;
        }

        return descriptor.ActionName switch
        {
            "GetComisionesVendedores" or "ExportComisionesExcel" or "ExportComisionesPdf" or "ExportComisionesBancoTxt"
                => ReporteComisionesFormulario(request),
            "GetLotesPago" or "DownloadLotePagoTxt" or "UpdateLotePagoEstado"
                => "Reporte de pagos generados",
            "GetReporteEnvios"
                => "Reporte de envio de pedidos",
            "GetResumenGerencial"
                => "Reporte resumen gerencial",
            _ => "Reportes"
        };
    }

    private static string ReporteComisionesFormulario(HttpRequest request)
    {
        var scope = request.Query.TryGetValue("scope", out var value)
            ? value.ToString()
            : string.Empty;

        return scope.ToLowerInvariant() switch
        {
            "externos" => "Reporte de comisiones de vendedores externos",
            "team-leaders" => "Reporte de comisiones de Team Leader",
            _ => "Reporte de comisiones por vendedor"
        };
    }
}
