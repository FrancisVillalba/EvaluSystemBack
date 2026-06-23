namespace EvaluSystemBack.Models;

public class Configuracion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int NroConfiguracion { get; set; }
    public string Valor { get; set; } = string.Empty;
}
