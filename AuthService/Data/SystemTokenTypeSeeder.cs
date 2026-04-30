using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante o catálogo canônico de SystemTokenTypes (idempotente).</summary>
public static class SystemTokenTypeSeeder
{
    /// <summary>Code do SystemTokenType padrão usado quando uma rota não especifica política JWT explícita.</summary>
    public const string DefaultCode = "default";

    private static readonly (string Code, string Name, string Description)[] SystemTokenTypes =
    [
        (DefaultCode, "Default", "Política JWT padrão para rotas autenticadas (Bearer JWT do usuário corrente).")
    ];

    public static async Task EnsureSystemTokenTypesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;

        foreach (var (code, name, description) in SystemTokenTypes)
        {
            var existing = await db.SystemTokenTypes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

            if (existing is null)
            {
                db.SystemTokenTypes.Add(new AppSystemTokenType
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
