namespace AuthService.Auth;

internal static class PermissionCatalog
{
    private const string AuthenticatorSystemCode = "authenticator";
    private const string KurttoSystemCode = "kurtto";

    /// <summary>
    /// Mapeamento canônico recurso lógico (PascalCase) → código do sistema oficial.
    /// Fonte única de verdade compartilhada entre <see cref="PermissionResolver"/> e geração reversa
    /// de codes legíveis em <c>verify-token</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ResourceToSystem =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Clients"] = AuthenticatorSystemCode,
            ["Users"] = AuthenticatorSystemCode,
            ["Systems"] = AuthenticatorSystemCode,
            ["SystemsRoutes"] = AuthenticatorSystemCode,
            ["SystemTokensTypes"] = AuthenticatorSystemCode,
            ["Permissions"] = AuthenticatorSystemCode,
            ["PermissionsTypes"] = AuthenticatorSystemCode,
            ["Roles"] = AuthenticatorSystemCode,
            ["Kurtto"] = KurttoSystemCode
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ResourcesBySystem =
        ResourceToSystem
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(kvp => kvp.Key).OrderBy(r => r, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

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

    /// <summary>
    /// Retorna a lista (ordenada) de recursos lógicos PascalCase associados a um <paramref name="systemCode"/>.
    /// Devolve coleção vazia quando o sistema é desconhecido.
    /// </summary>
    public static IReadOnlyList<string> GetResourcesForSystem(string systemCode)
    {
        return ResourcesBySystem.TryGetValue(systemCode, out var resources)
            ? resources
            : Array.Empty<string>();
    }
}
