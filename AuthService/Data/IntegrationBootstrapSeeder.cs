using AuthService.Models;
using AuthService.Security;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Usuário de integração com todas as permissões do catálogo (somente ambiente de teste).</summary>
public static class IntegrationBootstrapSeeder
{
    public const string Email = "integration.bootstrap@test";
    public const string Password = "SenhaSegura1!";

    public static async Task EnsureBootstrapUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
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
                Name = "Integration Bootstrap",
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
