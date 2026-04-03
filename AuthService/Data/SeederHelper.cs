using AuthService.Models;
using AuthService.Security;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Lógica compartilhada entre seeders que garantem um usuário com todas as permissões do catálogo.</summary>
internal static class SeederHelper
{
    internal static async Task EnsureUserWithAllPermissionsAsync(
        AppDbContext db, string email, string credential, string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        var utc = DateTime.UtcNow;
        User user;

        if (existing is null)
        {
            user = new User
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
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user = existing;
            var (_, newHash) = UserPasswordHasher.Verify(user, credential);
            if (newHash is not null)
            {
                user.Password = newHash;
                user.UpdatedAt = utc;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var allPermissionIds = await db.Permissions.AsNoTracking()
            .Select(p => p.Id).ToListAsync(cancellationToken);

        var existingLinks = (await db.UserPermissions.AsNoTracking()
            .Where(up => up.UserId == user.Id)
            .Select(up => up.PermissionId)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var pid in allPermissionIds)
        {
            if (existingLinks.Contains(pid))
                continue;

            db.UserPermissions.Add(new AppUserPermission
            {
                UserId = user.Id,
                PermissionId = pid,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
