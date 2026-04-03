namespace AuthService.Data;

/// <summary>Garante o usuário padrão do sistema (idempotente). Executar após <see cref="OfficialCatalogSeeder"/>.</summary>
public static class DefaultSystemUserSeeder
{
    public const string Email = "root@email.com.br";

    private const string CredentialEnvVar = "DEFAULT_SYSTEM_USER_PASSWORD";

    /// <summary>Resolve a credencial do usuário padrão a partir da variável de ambiente.</summary>
    public static string ResolveCredential()
    {
        var value = Environment.GetEnvironmentVariable(CredentialEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"A variável de ambiente '{CredentialEnvVar}' é obrigatória para o seed do usuário padrão.");
        return value;
    }

    public static Task EnsureDefaultUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
        => SeederHelper.EnsureUserWithAllPermissionsAsync(
            db, Email, ResolveCredential(), "Usuário padrão do sistema", cancellationToken);
}
