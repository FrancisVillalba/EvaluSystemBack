namespace EvaluSystemBack.Models;

public abstract class SimpleStringCatalog
{
    public string Id { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public bool? Estado { get; set; }
}
