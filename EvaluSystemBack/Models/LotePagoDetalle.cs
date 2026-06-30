namespace EvaluSystemBack.Models;

public class LotePagoDetalle
{
    public int Id { get; set; }
    public int LotePagoId { get; set; }
    public int UsuarioId { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public string CuentaDebitoEmpresa { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string EsAguinaldo { get; set; } = "NO";
    public DateTime FechaPago { get; set; }
    public string TipoCuenta { get; set; } = "CC";
    public string LineaTxt { get; set; } = string.Empty;

    public LotePago? LotePago { get; set; }
    public Usuario? Usuario { get; set; }
}
