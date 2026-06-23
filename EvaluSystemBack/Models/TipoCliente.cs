namespace EvaluSystemBack.Models;

public class TipoCliente : SimpleStringCatalog
{
    public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
}
