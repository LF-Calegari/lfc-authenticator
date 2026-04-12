using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Garante sistemas, tipos de permissão e permissões oficiais (idempotente).</summary>
public static class OfficialCatalogSeeder
{
    private static readonly (string Code, string Name)[] Systems =
    [
        ("clients", "Clients"),
        ("users", "Users"),
        ("systems", "Systems"),
        ("systems-routes", "Systems routes"),
        ("system-tokens-types", "System token types"),
        ("permissions", "Permissions"),
        ("permissions-types", "Permissions types"),
        ("roles", "Roles"),
        ("kurtto", "Kurtto")
    ];

    private static readonly (string Code, string Name)[] PermissionTypes =
    [
        ("create", "Create"),
        ("read", "Read"),
        ("update", "Update"),
        ("delete", "Delete"),
        ("restore", "Restore")
    ];

    public static async Task EnsureCatalogAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;

        foreach (var (code, name) in Systems)
        {
            var exists = await db.Systems.IgnoreQueryFilters().AnyAsync(s => s.Code == code, cancellationToken);
            if (exists)
                continue;

            db.Systems.Add(new AppSystem
            {
                Name = name,
                Code = code,
                Description = null,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (code, name) in PermissionTypes)
        {
            var exists = await db.PermissionTypes.IgnoreQueryFilters().AnyAsync(t => t.Code == code, cancellationToken);
            if (exists)
                continue;

            db.PermissionTypes.Add(new AppPermissionType
            {
                Name = name,
                Code = code,
                Description = null,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var systemCodeList = Systems.Select(s => s.Code).ToList();
        var typeCodeList = PermissionTypes.Select(t => t.Code).ToList();

        var systemIds = await db.Systems.AsNoTracking()
            .Where(s => systemCodeList.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Id, cancellationToken);

        var typeIds = await db.PermissionTypes.AsNoTracking()
            .Where(t => typeCodeList.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => t.Id, cancellationToken);

        foreach (var sys in Systems)
        {
            if (!systemIds.TryGetValue(sys.Code, out var systemId))
                continue;

            foreach (var pt in PermissionTypes)
            {
                if (!typeIds.TryGetValue(pt.Code, out var permissionTypeId))
                    continue;

                var has = await db.Permissions.IgnoreQueryFilters()
                    .AnyAsync(p => p.SystemId == systemId && p.PermissionTypeId == permissionTypeId, cancellationToken);
                if (has)
                    continue;

                db.Permissions.Add(new AppPermission
                {
                    SystemId = systemId,
                    PermissionTypeId = permissionTypeId,
                    Description = null,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
