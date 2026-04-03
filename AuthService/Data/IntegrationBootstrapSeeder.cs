namespace AuthService.Data;

/// <summary>Usuário de integração com todas as permissões do catálogo (somente ambiente de teste).</summary>
public static class IntegrationBootstrapSeeder
{
    public const string Email = "integration.bootstrap@test";

    private const string CredentialEnvVar = "INTEGRATION_BOOTSTRAP_PASSWORD";

    /// <summary>Resolve a credencial do usuário bootstrap a partir da variável de ambiente.</summary>
    public static string ResolveCredential()
    {
        var value = Environment.GetEnvironmentVariable(CredentialEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"A variável de ambiente '{CredentialEnvVar}' é obrigatória para o bootstrap seeder.");
        return value;
    }

    public static Task EnsureBootstrapUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
        => SeederHelper.EnsureUserWithAllPermissionsAsync(
            db, Email, ResolveCredential(), "Integration Bootstrap", cancellationToken);
}
