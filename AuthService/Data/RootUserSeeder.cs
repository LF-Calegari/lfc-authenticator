using AuthService.Models;
using AuthService.Security;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante o usuário root, seu cliente vinculado e a role root (idempotente).</summary>
public static class RootUserSeeder
{
    public const string RootEmail = "root@email.com.br";
    public const string RootName = "Root";
    public const string RootRoleCode = "root";
    public const string RootRoleName = "Root";

    private const string PasswordEnvVar = "DEFAULT_SYSTEM_USER_PASSWORD";

    public static async Task EnsureRootUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;
        var email = RootEmail.Trim().ToLowerInvariant();
        var password = ResolvePassword();

        var client = await EnsureClientAsync(db, email, utc, cancellationToken);
        var role = await EnsureRoleAsync(db, utc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var user = await EnsureUserAsync(db, email, password, client.Id, utc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await EnsureUserRoleAsync(db, user.Id, role.Id, utc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ResolvePassword()
    {
        var value = Environment.GetEnvironmentVariable(PasswordEnvVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"A variável de ambiente '{PasswordEnvVar}' é obrigatória para o seed do usuário root.");
        return value;
    }

    private static async Task<Client> EnsureClientAsync(
        AppDbContext db,
        string normalizedEmail,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existing = await (
            from u in db.Users.IgnoreQueryFilters()
            join c in db.Clients.IgnoreQueryFilters() on u.ClientId equals c.Id
            where u.Email == normalizedEmail
            select c
        ).FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.UpdatedAt = utc;
            }
            return existing;
        }

        var client = new Client
        {
            Type = "PF",
            FullName = RootName,
            Cpf = null,
            Cnpj = null,
            CorporateName = null,
            CreatedAt = utc,
            UpdatedAt = utc,
            DeletedAt = null
        };
        db.Clients.Add(client);
        return client;
    }

    private static async Task<AppRole> EnsureRoleAsync(
        AppDbContext db,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existing = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Code == RootRoleCode, cancellationToken);

        if (existing is not null)
        {
            existing.Name = RootRoleName;
            if (existing.DeletedAt is not null)
                existing.DeletedAt = null;
            existing.UpdatedAt = utc;
            return existing;
        }

        var role = new AppRole
        {
            Name = RootRoleName,
            Code = RootRoleCode,
            CreatedAt = utc,
            UpdatedAt = utc,
            DeletedAt = null
        };
        db.Roles.Add(role);
        return role;
    }

    private static async Task<User> EnsureUserAsync(
        AppDbContext db,
        string normalizedEmail,
        string password,
        Guid clientId,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        var existing = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existing is not null)
        {
            existing.Name = RootName;
            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.Active = true;
            }
            if (existing.ClientId is null || existing.ClientId == Guid.Empty)
                existing.ClientId = clientId;

            var (_, rehashed) = UserPasswordHasher.Verify(existing, password);
            if (rehashed is not null)
                existing.Password = rehashed;

            existing.UpdatedAt = utc;
            return existing;
        }

        var user = new User
        {
            Name = RootName,
            Email = normalizedEmail,
            Password = UserPasswordHasher.HashPlainPassword(password),
            ClientId = clientId,
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
}
