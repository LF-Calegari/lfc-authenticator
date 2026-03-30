using AuthService.Models;
using AuthService.Security;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante o usuário padrão do sistema (idempotente). Executar após <see cref="OfficialCatalogSeeder"/>.</summary>
public static class DefaultSystemUserSeeder
{
    public const string Email = "root@email.com.br";
    public const string Password = "toor";

    public static async Task EnsureDefaultUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = Email.Trim().ToLowerInvariant();
        var existing = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        var utc = DateTime.UtcNow;
        User user;

        if (existing is null)
        {
            user = new User
            {
                Name = "Usuário padrão do sistema",
                Email = normalizedEmail,
                Password = UserPasswordHasher.HashPlainPassword(Password),
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
            var (_, newHash) = UserPasswordHasher.Verify(user, Password);
            if (newHash is not null)
            {
                user.Password = newHash;
                user.UpdatedAt = utc;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var allPermissionIds = await db.Permissions.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken);

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
