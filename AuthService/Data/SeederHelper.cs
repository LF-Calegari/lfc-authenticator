using AuthService.Models;
using AuthService.Security;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Lógica compartilhada entre seeders de usuários/roles do sistema.</summary>
internal static class SeederHelper
{
    internal static async Task EnsureUserAssignedToRoleWithAllPermissionsAsync(
        AppDbContext db,
        string email,
        string credential,
        string displayName,
        string roleCode,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;
        var allPermissionIds = await db.Permissions.AsNoTracking()
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (allPermissionIds.Count == 0)
            throw new InvalidOperationException("Nenhuma permissão cadastrada para vincular às roles do sistema.");

        var role = await EnsureRoleAsync(db, roleCode, roleName, utc, cancellationToken);
        await EnsureRolePermissionsAsync(db, role.Id, allPermissionIds, utc, cancellationToken);

        var user = await EnsureUserAsync(db, email, credential, displayName, utc, cancellationToken);
        await EnsureUserRoleAsync(db, user.Id, role.Id, utc, cancellationToken);
        await DisableDirectUserPermissionsAsync(db, user.Id, utc, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<User> EnsureUserAsync(
        AppDbContext db,
        string email,
        string credential,
        string displayName,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existing is null)
        {
            var user = new User
            {
                Name = displayName,
                Email = normalizedEmail,
                Password = UserPasswordHasher.HashPlainPassword(credential),
                Identity = 0,
                Active = true,
                TokenVersion = 0,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            };
            db.Users.Add(user);
            return user;
        }

        existing.Name = displayName;
        if (existing.DeletedAt is not null)
        {
            existing.DeletedAt = null;
            existing.Active = true;
            existing.UpdatedAt = utc;
        }

        var (_, newHash) = UserPasswordHasher.Verify(existing, credential);
        if (newHash is not null)
        {
            existing.Password = newHash;
            existing.UpdatedAt = utc;
        }

        return existing;
    }

    private static async Task<AppRole> EnsureRoleAsync(
        AppDbContext db,
        string roleCode,
        string roleName,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existing = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Code == roleCode, cancellationToken);

        if (existing is null)
        {
            var role = new AppRole
            {
                Name = roleName,
                Code = roleCode,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            };
            db.Roles.Add(role);
            return role;
        }

        existing.Name = roleName;
        if (existing.DeletedAt is not null)
            existing.DeletedAt = null;
        existing.UpdatedAt = utc;
        return existing;
    }

    private static async Task EnsureRolePermissionsAsync(
        AppDbContext db,
        Guid roleId,
        IReadOnlyCollection<Guid> permissionIds,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existingLinks = await db.RolePermissions.IgnoreQueryFilters()
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);

        var byPermissionId = existingLinks.ToDictionary(x => x.PermissionId);

        foreach (var permissionId in permissionIds)
        {
            if (byPermissionId.TryGetValue(permissionId, out var link))
            {
                if (link.DeletedAt is not null)
                {
                    link.DeletedAt = null;
                    link.UpdatedAt = utc;
                }

                continue;
            }

            db.RolePermissions.Add(new AppRolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }
    }

    private static async Task EnsureUserRoleAsync(
        AppDbContext db,
        Guid userId,
        Guid roleId,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existing = await db.UserRoles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, cancellationToken);

        if (existing is null)
        {
            db.UserRoles.Add(new AppUserRole
            {
                UserId = userId,
                RoleId = roleId,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
            return;
        }

        if (existing.DeletedAt is not null)
        {
            existing.DeletedAt = null;
            existing.UpdatedAt = utc;
        }
    }

    private static async Task DisableDirectUserPermissionsAsync(
        AppDbContext db,
        Guid userId,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var directLinks = await db.UserPermissions.IgnoreQueryFilters()
            .Where(up => up.UserId == userId && up.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var link in directLinks)
        {
            link.DeletedAt = utc;
            link.UpdatedAt = utc;
        }
    }
}
