using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlException)
        {
            _logger.LogWarning(ex, "Error de base de datos.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = GetSqlMessage(sqlException)
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Error de validacion.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Ocurrio un error inesperado."
            });
        }
    }

    private static string GetSqlMessage(SqlException exception)
    {
        return exception.Number switch
        {
            2627 or 2601 => "Ya existe un registro con esos datos.",
            547 => "No se puede guardar o eliminar porque hay datos relacionados o una regla de validacion no se cumple.",
            515 => "Hay campos obligatorios sin completar.",
            _ => exception.Message
        };
    }
}
