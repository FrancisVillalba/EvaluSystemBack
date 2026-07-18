namespace EvaluSystemBack.Options;

public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 30;
    public int RefreshExpirationHours { get; set; } = 8;
}
