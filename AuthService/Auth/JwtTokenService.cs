using JWT.Algorithms;
using JWT.Builder;
using Microsoft.Extensions.Options;

namespace AuthService.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public string CreateAccessToken(Guid userId, int tokenVersion, Guid systemId, out DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(_options.Secret) || _options.Secret.Length < 32)
            throw new InvalidOperationException("Auth:Jwt:Secret deve ter pelo menos 32 caracteres.");

        var minutes = _options.ExpirationMinutes <= 0 ? 60 : _options.ExpirationMinutes;
        expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var exp = expiresAt.ToUnixTimeSeconds();

        return JwtBuilder.Create()
            .WithAlgorithm(new HMACSHA256Algorithm())
            .WithSecret(_options.Secret)
            .AddClaim("sub", userId.ToString("D"))
            .AddClaim("tv", tokenVersion)
            .AddClaim("sys", systemId.ToString("D"))
            .AddClaim("exp", exp)
            .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .Encode();
    }
}
