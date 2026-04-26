namespace AuthService.Auth;

public interface IJwtTokenService
{
    /// <summary>
    /// Emite JWT com claims <c>sub</c> (user id), <c>tv</c> (token version) e
    /// <c>sys</c> (system id de quem fez login — usado para amarrar o token a um sistema específico).
    /// </summary>
    string CreateAccessToken(Guid userId, int tokenVersion, Guid systemId, out DateTimeOffset expiresAt);
}
