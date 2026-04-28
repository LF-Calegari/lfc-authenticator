namespace AuthService.Auth;

internal static class PermissionCatalog
{
    private const string AuthenticatorSystemCode = "authenticator";

    /// <summary>
    /// Mapeamento canônico recurso lógico (PascalCase) → código do sistema oficial.
    /// Fonte única de verdade compartilhada entre <see cref="PermissionResolver"/> e geração reversa
    /// de codes legíveis em <c>verify-token</c>.
    /// </summary>
    private static readonly Dictionary<string, string> ResourceToSystem =
        new(StringComparer.Ordinal)
        {
            ["Clients"] = AuthenticatorSystemCode,
            ["Users"] = AuthenticatorSystemCode,
            ["Systems"] = AuthenticatorSystemCode,
            ["SystemsRoutes"] = AuthenticatorSystemCode,
            ["SystemTokensTypes"] = AuthenticatorSystemCode,
            ["Permissions"] = AuthenticatorSystemCode,
            ["PermissionsTypes"] = AuthenticatorSystemCode,
            ["Roles"] = AuthenticatorSystemCode
        };

    /// <summary>Mapeia o prefixo da chave (ex.: Users, SystemsRoutes) para o código do sistema no cadastro oficial.</summary>
    public static bool TryGetSystemCode(string resourcePascal, out string systemCode)
    {
        if (ResourceToSystem.TryGetValue(resourcePascal, out var resolved))
        {
            systemCode = resolved;
            return true;
        }

        systemCode = string.Empty;
        return false;
    }
}
