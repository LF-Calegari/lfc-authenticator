namespace AuthService.Auth;

internal static class PermissionCatalog
{
    /// <summary>Mapeia o prefixo da chave (ex.: Users, SystemsRoutes) para o código do sistema no cadastro oficial.</summary>
    public static bool TryGetSystemCode(string resourcePascal, out string systemCode)
    {
        systemCode = resourcePascal switch
        {
            "Clients" => "clients",
            "Users" => "users",
            "Systems" => "systems",
            "SystemsRoutes" => "systems-routes",
            "SystemTokensTypes" => "system-tokens-types",
            "Permissions" => "permissions",
            "PermissionsTypes" => "permissions-types",
            "Roles" => "roles",
            "Kurtto" => "kurtto",
            _ => ""
        };

        return systemCode.Length > 0;
    }
}
