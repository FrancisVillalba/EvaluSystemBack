using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;

namespace EvaluSystemBack.Controllers;

public class TiposClienteController : CrudControllerBase<TipoCliente, string>
{
    public TiposClienteController(IGenericService<TipoCliente> service) : base(service)
    {
    }
}
