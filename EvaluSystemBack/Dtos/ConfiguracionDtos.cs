using System.ComponentModel.DataAnnotations;

namespace EvaluSystemBack.Dtos;

public record ConfiguracionDto(int Id, string Nombre, int NroConfiguracion, string Valor);

public record ConfiguracionRequest([Required] string Nombre, int NroConfiguracion, [Required] string Valor);
