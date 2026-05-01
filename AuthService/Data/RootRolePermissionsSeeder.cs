using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>
/// Vincula a role root a todas as permissões do sistema Authenticator (idempotente).
/// Pré-requisitos: SystemSeeder, AuthenticatorRoutesSeeder, PermissionTypeSeeder,
/// AuthenticatorPermissionsSeeder e RootUserSeeder.
/// </summary>
public static class RootRolePermissionsSeeder
{
    private const string SystemCode = "authenticator";

    public static async Task EnsureRootRolePermissionsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var systemId = await db.Systems.AsNoTracking()
            .Where(s => s.Code == SystemCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId is null || systemId == Guid.Empty)
            throw new InvalidOperationException(
                $"Sistema '{SystemCode}' não encontrado. Execute o SystemSeeder antes do RootRolePermissionsSeeder.");

        var rootRoleId = await db.Roles.AsNoTracking()
            .Where(r => r.SystemId == systemId.Value && r.Code == RootUserSeeder.RootRoleCode)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (rootRoleId is null || rootRoleId == Guid.Empty)
            throw new InvalidOperationException(
                $"Role '{RootUserSeeder.RootRoleCode}' não encontrada no sistema '{SystemCode}'. Execute o RootUserSeeder antes do RootRolePermissionsSeeder.");

        var permissionIds = await (
            from p in db.Permissions
            join r in db.Routes on p.RouteId equals r.Id
            where r.SystemId == systemId.Value
            select p.Id
        ).ToListAsync(cancellationToken);

        var existing = await db.RolePermissions.IgnoreQueryFilters()
            .Where(rp => rp.RoleId == rootRoleId.Value)
            .ToDictionaryAsync(rp => rp.PermissionId, cancellationToken);

        var utc = DateTime.UtcNow;

        foreach (var permissionId in permissionIds)
        {
            if (!existing.TryGetValue(permissionId, out var current))
            {
                db.RolePermissions.Add(new AppRolePermission
                {
                    RoleId = rootRoleId.Value,
                    PermissionId = permissionId,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
                continue;
            }

            if (current.DeletedAt is not null)
            {
                current.DeletedAt = null;
                current.UpdatedAt = utc;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
