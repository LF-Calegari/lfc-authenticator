using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>
/// Seed idempotente do sistema Kurtto (lfc-kurtto): rotas da API versionada e papel com permissões do catálogo oficial.
/// Executar após <see cref="OfficialCatalogSeeder"/>.
/// </summary>
public static class KurttoAccessSeeder
{
    public const string KurttoAdminRoleCode = "kurtto-admin";

    /// <summary>
    /// Códigos únicos em <see cref="AppRoute.Code"/> para as rotas da API Kurtto sob <c>/api/v1</c>
    /// que exigem credencial (hoje <c>X-Admin-Secret</c> no Kurtto; alinhadas a <c>perm:Kurtto.*</c> quando houver JWT).
    /// </summary>
    public static readonly IReadOnlyList<string> KurttoAuthenticatedRouteCodes =
    [
        "KURTTO_V1_URLS_LIST_INCLUDE_DELETED",
        "KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED",
        "KURTTO_V1_URLS_PATCH_RESTORE"
    ];

    private static readonly (string Code, string Name, string Description)[] AuthenticatedRoutes =
    [
        (
            "KURTTO_V1_URLS_LIST_INCLUDE_DELETED",
            "GET /api/v1/urls (include_deleted)",
            "Lista URLs incluindo soft-deleted; exige X-Admin-Secret no Kurtto. Permissão alvo: perm:Kurtto.Read."
        ),
        (
            "KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED",
            "GET /api/v1/urls/{code} (include_deleted)",
            "Obtém URL por código incluindo soft-deleted; exige X-Admin-Secret. Permissão alvo: perm:Kurtto.Read."
        ),
        (
            "KURTTO_V1_URLS_PATCH_RESTORE",
            "PATCH /api/v1/urls/{code}/restore",
            "Reativa URL soft-deleted; exige X-Admin-Secret. Permissão alvo: perm:Kurtto.Restore."
        )
    ];

    public static async Task EnsureKurttoAccessAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var systemId = await db.Systems.AsNoTracking()
            .Where(s => s.Code == "kurtto")
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId is null)
            throw new InvalidOperationException(
                "Sistema 'kurtto' não encontrado. Execute OfficialCatalogSeeder antes de KurttoAccessSeeder.");

        var utc = DateTime.UtcNow;

        foreach (var (code, name, description) in AuthenticatedRoutes)
        {
            var exists = await db.Routes.IgnoreQueryFilters()
                .AnyAsync(r => r.Code == code, cancellationToken);
            if (exists)
                continue;

            db.Routes.Add(new AppRoute
            {
                SystemId = systemId.Value,
                Name = name,
                Code = code,
                Description = description,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Code == KurttoAdminRoleCode, cancellationToken);

        if (role is null)
        {
            role = new AppRole
            {
                Name = "Kurtto Admin",
                Code = KurttoAdminRoleCode,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (role.DeletedAt is not null)
        {
            role.DeletedAt = null;
            role.UpdatedAt = utc;
            await db.SaveChangesAsync(cancellationToken);
        }

        var kurttoPermissionIds = await db.Permissions.AsNoTracking()
            .Where(p => p.SystemId == systemId.Value)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (kurttoPermissionIds.Count == 0)
            throw new InvalidOperationException("Nenhuma permissão do sistema 'kurtto' encontrada no catálogo.");

        foreach (var pid in kurttoPermissionIds)
        {
            var existing = await db.RolePermissions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    rp => rp.RoleId == role.Id && rp.PermissionId == pid,
                    cancellationToken);

            if (existing is null)
            {
                db.RolePermissions.Add(new AppRolePermission
                {
                    RoleId = role.Id,
                    PermissionId = pid,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                });
            }
            else if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.UpdatedAt = utc;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
