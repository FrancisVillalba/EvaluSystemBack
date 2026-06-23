using EvaluSystemBack.Models;
using EvaluSystemBack.Services.Interfaces;

namespace EvaluSystemBack.Controllers;

public class TiposDocumentoController : CrudControllerBase<TipoDocumento, string>
{
    public TiposDocumentoController(IGenericService<TipoDocumento> service) : base(service)
    {
    }
}
