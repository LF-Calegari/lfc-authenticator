namespace AuthService.Auth;

/// <summary>Nomes de políticas <c>Authorize(Policy = ...)</c> (prefixo perm:).</summary>
public static class PermissionPolicies
{
    public const string ClientsCreate = "perm:Clients.Create";
    public const string ClientsRead = "perm:Clients.Read";
    public const string ClientsUpdate = "perm:Clients.Update";
    public const string ClientsDelete = "perm:Clients.Delete";
    public const string ClientsRestore = "perm:Clients.Restore";

    public const string UsersCreate = "perm:Users.Create";
    public const string UsersRead = "perm:Users.Read";
    public const string UsersUpdate = "perm:Users.Update";
    public const string UsersDelete = "perm:Users.Delete";
    public const string UsersRestore = "perm:Users.Restore";

    public const string SystemsCreate = "perm:Systems.Create";
    public const string SystemsRead = "perm:Systems.Read";
    public const string SystemsUpdate = "perm:Systems.Update";
    public const string SystemsDelete = "perm:Systems.Delete";
    public const string SystemsRestore = "perm:Systems.Restore";

    public const string SystemsRoutesCreate = "perm:SystemsRoutes.Create";
    public const string SystemsRoutesRead = "perm:SystemsRoutes.Read";
    public const string SystemsRoutesUpdate = "perm:SystemsRoutes.Update";
    public const string SystemsRoutesDelete = "perm:SystemsRoutes.Delete";
    public const string SystemsRoutesRestore = "perm:SystemsRoutes.Restore";

    public const string SystemTokensTypesCreate = "perm:SystemTokensTypes.Create";
    public const string SystemTokensTypesRead = "perm:SystemTokensTypes.Read";
    public const string SystemTokensTypesUpdate = "perm:SystemTokensTypes.Update";
    public const string SystemTokensTypesDelete = "perm:SystemTokensTypes.Delete";
    public const string SystemTokensTypesRestore = "perm:SystemTokensTypes.Restore";

    public const string PermissionsCreate = "perm:Permissions.Create";
    public const string PermissionsRead = "perm:Permissions.Read";
    public const string PermissionsUpdate = "perm:Permissions.Update";
    public const string PermissionsDelete = "perm:Permissions.Delete";
    public const string PermissionsRestore = "perm:Permissions.Restore";

    public const string PermissionsTypesCreate = "perm:PermissionsTypes.Create";
    public const string PermissionsTypesRead = "perm:PermissionsTypes.Read";
    public const string PermissionsTypesUpdate = "perm:PermissionsTypes.Update";
    public const string PermissionsTypesDelete = "perm:PermissionsTypes.Delete";
    public const string PermissionsTypesRestore = "perm:PermissionsTypes.Restore";

    public const string RolesCreate = "perm:Roles.Create";
    public const string RolesRead = "perm:Roles.Read";
    public const string RolesUpdate = "perm:Roles.Update";
    public const string RolesDelete = "perm:Roles.Delete";
    public const string RolesRestore = "perm:Roles.Restore";
}
