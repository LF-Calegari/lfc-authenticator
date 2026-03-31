using AuthService.Models;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Security;

/// <summary>
/// Hash e verificação de senha (PBKDF2 via <see cref="PasswordHasher{TUser}"/>).
/// Suporta migração de valores legados em texto plano: em caso de match, devolve hash para persistir.
/// </summary>
public static class UserPasswordHasher
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static string HashPlainPassword(string plainPassword) =>
        Hasher.HashPassword(new User(), plainPassword);

    /// <returns>Sucesso da autenticação e, se necessário, novo valor a gravar em <see cref="User.Password"/>.</returns>
    public static (bool Success, string? NewStoredPassword) Verify(User user, string plainPassword)
    {
        try
        {
            var result = Hasher.VerifyHashedPassword(user, user.Password, plainPassword);
            if (result == PasswordVerificationResult.Success)
                return (true, null);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                return (true, Hasher.HashPassword(user, plainPassword));
        }
        catch (FormatException)
        {
            // Valor legado em texto plano/não-hash: segue para fallback compatível.
        }

        if (user.Password == plainPassword)
            return (true, Hasher.HashPassword(user, plainPassword));

        return (false, null);
    }
}
