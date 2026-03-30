using AuthService.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Auth;

public static class EffectivePermissionIds
{
    public static async Task<HashSet<Guid>> GetForUserAsync(AppDbContext db, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var direct = await db.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == userId)
            .Select(up => up.PermissionId)
            .ToListAsync(cancellationToken);

        var viaRoles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                db.RolePermissions.AsNoTracking(),
                ur => ur.RoleId,
                rp => rp.RoleId,
                (_, rp) => rp.PermissionId)
            .ToListAsync(cancellationToken);

        return direct.Concat(viaRoles).ToHashSet();
    }
}
