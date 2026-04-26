using System.Security.Claims;
using System.Text.Encodings.Web;
using AuthService.Data;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace AuthService.Auth;

public sealed class JwtBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<JwtOptions> jwtOptions,
    IServiceProvider services)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly IServiceProvider _services = services;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenReadResult = TryReadBearerToken(out var token);
        if (tokenReadResult is not null)
            return tokenReadResult;

        var secretValidation = ValidateJwtSecret();
        if (secretValidation is not null)
            return secretValidation;

        var payloadResult = TryDecodePayload(token);
        if (payloadResult.Error is not null)
            return payloadResult.Error;

        var claimExtraction = TryExtractIdentityClaims(
            payloadResult.Payload!,
            out var userId,
            out var tokenVersionClaim,
            out var systemIdClaim);
        if (claimExtraction is not null)
            return claimExtraction;

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return AuthenticateResult.Fail("Usuário não encontrado.");

        if (!user.Active)
            return AuthenticateResult.Fail("Usuário inativo.");

        if (user.TokenVersion != tokenVersionClaim)
            return AuthenticateResult.Fail("Token revogado.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(SystemIdClaimType, systemIdClaim.ToString("D"))
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// Tipo de claim que carrega o <c>systemId</c> do sistema que emitiu o token (claim <c>sys</c>).
    /// Usado pelo verify-token para evitar uso cruzado de tokens entre sistemas distintos.
    /// </summary>
    public const string SystemIdClaimType = "sys";

    private AuthenticateResult? TryReadBearerToken(out string token)
    {
        token = string.Empty;
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return AuthenticateResult.NoResult();

        var auth = values.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Cabeçalho Authorization inválido.");

        token = auth["Bearer ".Length..].Trim();
        return string.IsNullOrEmpty(token) ? AuthenticateResult.Fail("Token ausente.") : null;
    }

    private AuthenticateResult? ValidateJwtSecret()
    {
        if (!string.IsNullOrWhiteSpace(_jwt.Secret) && _jwt.Secret.Length >= 32)
            return null;

        Logger.LogError("Auth:Jwt:Secret inválido ou ausente.");
        return AuthenticateResult.Fail("Configuração de autenticação inválida.");
    }

    private (IDictionary<string, object>? Payload, AuthenticateResult? Error) TryDecodePayload(string token)
    {
        try
        {
            var payload = JwtBuilder.Create()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(_jwt.Secret)
                .MustVerifySignature()
                .Decode<IDictionary<string, object>>(token);
            return (payload, null);
        }
        catch (TokenExpiredException)
        {
            return (null, AuthenticateResult.Fail("Token expirado."));
        }
        catch (SignatureVerificationException)
        {
            return (null, AuthenticateResult.Fail("Assinatura do token inválida."));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Falha ao validar JWT.");
            return (null, AuthenticateResult.Fail("Token inválido."));
        }
    }

    private static AuthenticateResult? TryExtractIdentityClaims(
        IDictionary<string, object> payload,
        out Guid userId,
        out int tokenVersionClaim,
        out Guid systemIdClaim)
    {
        userId = Guid.Empty;
        tokenVersionClaim = 0;
        systemIdClaim = Guid.Empty;

        if (!payload.TryGetValue("sub", out var subObj) || subObj?.ToString() is not { } subStr
            || !Guid.TryParse(subStr, out userId))
            return AuthenticateResult.Fail("Token sem identificador de usuário válido.");

        if (!payload.TryGetValue("tv", out var tvObj) || !TryGetInt32(tvObj, out tokenVersionClaim))
            return AuthenticateResult.Fail("Token sem versão de sessão válida.");

        if (!payload.TryGetValue("sys", out var sysObj) || sysObj?.ToString() is not { } sysStr
            || !Guid.TryParse(sysStr, out systemIdClaim))
            return AuthenticateResult.Fail("Token sem identificador de sistema válido.");

        return null;
    }

    private static bool TryGetInt32(object? value, out int n)
    {
        n = 0;
        switch (value)
        {
            case int i:
                n = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                n = (int)l;
                return true;
            case double d when d is >= int.MinValue and <= int.MaxValue:
                n = (int)d;
                return true;
            default:
                return int.TryParse(
                    value?.ToString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out n);
        }
    }
}
