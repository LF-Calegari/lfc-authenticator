using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>
/// Garante uma permissão por rota do sistema Authenticator no tipo natural inferido pelo Code da rota
/// (idempotente). Pré-requisitos: SystemSeeder, AuthenticatorRoutesSeeder e PermissionTypeSeeder.
/// </summary>
public static class AuthenticatorPermissionsSeeder
{
    private const string SystemCode = "authenticator";

    private static readonly string[] RequiredPermissionTypeCodes =
        ["create", "read", "update", "delete", "restore"];

    public static async Task EnsurePermissionsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var systemId = await db.Systems.AsNoTracking()
            .Where(s => s.Code == SystemCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId is null || systemId == Guid.Empty)
            throw new InvalidOperationException(
                $"Sistema '{SystemCode}' não encontrado. Execute o SystemSeeder antes do AuthenticatorPermissionsSeeder.");

        var permissionTypes = await db.PermissionTypes
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Code, cancellationToken);

        foreach (var requiredCode in RequiredPermissionTypeCodes)
        {
            if (!permissionTypes.ContainsKey(requiredCode))
                throw new InvalidOperationException(
                    $"Tipo de permissão '{requiredCode}' não encontrado. Execute o PermissionTypeSeeder antes do AuthenticatorPermissionsSeeder.");
        }

        var routes = await db.Routes
            .Where(r => r.SystemId == systemId.Value)
            .ToListAsync(cancellationToken);

        var utc = DateTime.UtcNow;

        foreach (var route in routes)
        {
            var permissionTypeCode = ResolvePermissionTypeCode(route.Code);
            var permissionType = permissionTypes[permissionTypeCode];
            var description = $"{permissionType.Name}: {route.Name}";

            var existing = await db.Permissions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    p => p.RouteId == route.Id && p.PermissionTypeId == permissionType.Id,
                    cancellationToken);

            if (existing is null)
            {
                db.Permissions.Add(new AppPermission
                {
                    RouteId = route.Id,
                    PermissionTypeId = permissionType.Id,
                    Description = description,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
                continue;
            }

            existing.Description = description;
            if (existing.DeletedAt is not null)
                existing.DeletedAt = null;
            existing.UpdatedAt = utc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Endpoints especiais (verify-token, permissions, logout) caem no fallback "read" — são consultas
    // sob a perspectiva do consumidor.
    private static string ResolvePermissionTypeCode(string routeCode)
    {
        if (routeCode.EndsWith("_CREATE", StringComparison.Ordinal))
            return "create";
        if (routeCode.EndsWith("_DELETE", StringComparison.Ordinal))
            return "delete";
        if (routeCode.EndsWith("_RESTORE", StringComparison.Ordinal))
            return "restore";
        if (routeCode.EndsWith("_UPDATE", StringComparison.Ordinal) ||
            routeCode.EndsWith("_UPDATE_PASSWORD", StringComparison.Ordinal))
            return "update";
        if (routeCode.EndsWith("_LIST", StringComparison.Ordinal) ||
            routeCode.EndsWith("_GET_BY_ID", StringComparison.Ordinal))
            return "read";

        return "read";
    }
}
