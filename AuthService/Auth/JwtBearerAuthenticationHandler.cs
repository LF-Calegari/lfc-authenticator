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
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return AuthenticateResult.NoResult();

        var auth = values.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Cabeçalho Authorization inválido.");

        var token = auth["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Token ausente.");

        if (string.IsNullOrWhiteSpace(_jwt.Secret) || _jwt.Secret.Length < 32)
        {
            Logger.LogError("Auth:Jwt:Secret inválido ou ausente.");
            return AuthenticateResult.Fail("Configuração de autenticação inválida.");
        }

        IDictionary<string, object> payload;
        try
        {
            payload = JwtBuilder.Create()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(_jwt.Secret)
                .MustVerifySignature()
                .Decode<IDictionary<string, object>>(token);
        }
        catch (TokenExpiredException)
        {
            return AuthenticateResult.Fail("Token expirado.");
        }
        catch (SignatureVerificationException)
        {
            return AuthenticateResult.Fail("Assinatura do token inválida.");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Falha ao validar JWT.");
            return AuthenticateResult.Fail("Token inválido.");
        }

        if (!payload.TryGetValue("sub", out var subObj) || subObj?.ToString() is not { } subStr
            || !Guid.TryParse(subStr, out var userId))
            return AuthenticateResult.Fail("Token sem identificador de usuário válido.");

        if (!payload.TryGetValue("tv", out var tvObj) || !TryGetInt32(tvObj, out var tokenVersionClaim))
            return AuthenticateResult.Fail("Token sem versão de sessão válida.");

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
            new(ClaimTypes.Name, user.Name)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
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
