using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante o cadastro do sistema oficial Authenticator (idempotente).</summary>
public static class SystemSeeder
{
    private static readonly (string Code, string Name, string Description)[] Systems =
    [
        ("authenticator", "Authenticator",
            "Sistema central de autenticação e autorização: gerencia clientes, usuários, roles, permissões e a emissão de JWT.")
    ];

    public static async Task EnsureSystemsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;

        foreach (var (code, name, description) in Systems)
        {
            var existing = await db.Systems.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Code == code, cancellationToken);

            if (existing is null)
            {
                db.Systems.Add(new AppSystem
                {
                    Name = name,
                    Code = code,
                    Description = description,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
                continue;
            }

            existing.Name = name;
            existing.Description = description;
            if (existing.DeletedAt is not null)
                existing.DeletedAt = null;
            existing.UpdatedAt = utc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
