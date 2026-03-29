namespace AuthService.Auth;

public interface IJwtTokenService
{
    /// <summary>Emite JWT com claims <c>sub</c> (user id) e <c>tv</c> (token version).</summary>
    string CreateAccessToken(Guid userId, int tokenVersion, out DateTimeOffset expiresAt);
}
