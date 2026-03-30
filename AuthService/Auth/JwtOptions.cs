namespace AuthService.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    /// <summary>Segredo HMAC (mínimo recomendado: 32 caracteres).</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Validade do access token em minutos.</summary>
    public int ExpirationMinutes { get; set; } = 60;
}
