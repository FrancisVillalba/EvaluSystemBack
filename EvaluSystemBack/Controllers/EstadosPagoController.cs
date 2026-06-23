using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;

namespace EvaluSystemBack.Controllers;

public class EstadosPagoController : CrudControllerBase<EstadoPago, string>
{
    public EstadosPagoController(IGenericService<EstadoPago> service) : base(service)
    {
    }
}
