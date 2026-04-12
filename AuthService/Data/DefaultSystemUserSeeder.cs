namespace AuthService.Data;

/// <summary>Garante os usuários base do sistema (idempotente). Executar após <see cref="OfficialCatalogSeeder"/>.</summary>
public static class DefaultSystemUserSeeder
{
    public const string RootEmail = "root@email.com.br";
    public const string AdminEmail = "admin@email.com.br";
    public const string DefaultEmail = "default@email.com.br";

    private const string RootCredentialEnvVar = "DEFAULT_SYSTEM_USER_PASSWORD";
    private const string AdminCredentialEnvVar = "ADMIN_SYSTEM_USER_PASSWORD";
    private const string DefaultCredentialEnvVar = "DEFAULT_USER_PASSWORD";

    public const string RootRoleCode = "root";
    public const string AdminRoleCode = "admin";
    public const string DefaultRoleCode = "default";

    private static readonly (string Name, string Email, string CredentialEnvVar, string RoleCode, string RoleName)[] SystemUsers =
    [
        ("Root do sistema", RootEmail, RootCredentialEnvVar, RootRoleCode, "Root"),
        ("Admin do sistema", AdminEmail, AdminCredentialEnvVar, AdminRoleCode, "Admin"),
        ("Usuário default do sistema", DefaultEmail, DefaultCredentialEnvVar, DefaultRoleCode, "Default")
    ];

    private static string ResolveCredentialByEnv(string credentialEnvVar)
    {
        var value = Environment.GetEnvironmentVariable(credentialEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"A variável de ambiente '{credentialEnvVar}' é obrigatória para o seed dos usuários do sistema.");
        return value;
    }

    /// <summary>Compatibilidade: credencial do usuário root.</summary>
    public static string ResolveCredential() => ResolveCredentialByEnv(RootCredentialEnvVar);

    public static Task EnsureDefaultUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
        => EnsureSystemUsersAsync(db, cancellationToken);

    public static async Task EnsureSystemUsersAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        foreach (var (name, email, credentialEnvVar, roleCode, roleName) in SystemUsers)
        {
            await SeederHelper.EnsureUserAssignedToRoleWithAllPermissionsAsync(
                db,
                email,
                ResolveCredentialByEnv(credentialEnvVar),
                name,
                roleCode,
                roleName,
                cancellationToken);
        }
    }
}
