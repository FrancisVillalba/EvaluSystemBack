using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;

namespace EvaluSystemBack.Controllers;

public class EstadosVentaController : CrudControllerBase<EstadoVenta, string>
{
    public EstadosVentaController(IGenericService<EstadoVenta> service) : base(service)
    {
    }
}
