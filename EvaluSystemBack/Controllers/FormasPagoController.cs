using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;

namespace EvaluSystemBack.Controllers;

public class FormasPagoController : CrudControllerBase<FormaPago, string>
{
    public FormasPagoController(IGenericService<FormaPago> service) : base(service)
    {
    }
}
