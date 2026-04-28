using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante os tipos canônicos de permissão (idempotente).</summary>
public static class PermissionTypeSeeder
{
    private static readonly (string Code, string Name, string Description)[] PermissionTypes =
    [
        ("create", "Create", "Permite criar novos registros do recurso."),
        ("read", "Read", "Permite consultar e listar registros do recurso."),
        ("update", "Update", "Permite atualizar registros existentes do recurso."),
        ("delete", "Delete", "Permite remover registros do recurso (soft-delete)."),
        ("restore", "Restore", "Permite restaurar registros do recurso previamente removidos.")
    ];

    public static async Task EnsurePermissionTypesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;

        foreach (var (code, name, description) in PermissionTypes)
        {
            var existing = await db.PermissionTypes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

            if (existing is null)
            {
                db.PermissionTypes.Add(new AppPermissionType
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
