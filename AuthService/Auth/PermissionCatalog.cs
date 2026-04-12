namespace AuthService.Auth;

internal static class PermissionCatalog
{
    private const string AuthenticatorSystemCode = "authenticator";
    private const string KurttoSystemCode = "kurtto";

    /// <summary>Mapeia o prefixo da chave (ex.: Users, SystemsRoutes) para o código do sistema no cadastro oficial.</summary>
    public static bool TryGetSystemCode(string resourcePascal, out string systemCode)
    {
        systemCode = resourcePascal switch
        {
            // Só existem 2 sistemas oficiais no catálogo: authenticator e kurtto.
            // Estes nomes são recursos/políticas internos do sistema authenticator.
            "Clients" => AuthenticatorSystemCode,
            "Users" => AuthenticatorSystemCode,
            "Systems" => AuthenticatorSystemCode,
            "SystemsRoutes" => AuthenticatorSystemCode,
            "SystemTokensTypes" => AuthenticatorSystemCode,
            "Permissions" => AuthenticatorSystemCode,
            "PermissionsTypes" => AuthenticatorSystemCode,
            "Roles" => AuthenticatorSystemCode,
            "Kurtto" => KurttoSystemCode,
            _ => ""
        };

        return systemCode.Length > 0;
    }
}
