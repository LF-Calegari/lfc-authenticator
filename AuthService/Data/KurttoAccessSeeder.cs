using AuthService.Auth;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>
/// Seed idempotente do sistema Kurtto (lfc-kurtto): rotas da API versionada no catálogo oficial.
/// Executar após <see cref="OfficialCatalogSeeder"/>.
/// </summary>
public static class KurttoAccessSeeder
{
    /// <summary>
    /// Contrato com <c>requireAdminOperation</c> em <c>lfc-kurtto</c> (<c>src/controllers/UrlController.ts</c>).
    /// Cobertura 100% das superfícies que exigem <c>X-Admin-Secret</c> na API versionada.
    /// </summary>
    public sealed record KurttoAdminRouteDefinition(
        string Code,
        string Name,
        string Description,
        string TargetPermissionPolicy);

    private static readonly KurttoAdminRouteDefinition[] AdminRouteDefinitions =
    [
        new(
            "KURTTO_V1_URLS_LIST_INCLUDE_DELETED",
            "GET /api/v1/urls (include_deleted)",
            "Lista URLs incluindo soft-deleted; exige X-Admin-Secret no Kurtto.",
            PermissionPolicies.KurttoRead),
        new(
            "KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED",
            "GET /api/v1/urls/{code} (include_deleted)",
            "Obtém URL por código incluindo soft-deleted; exige X-Admin-Secret.",
            PermissionPolicies.KurttoRead),
        new(
            "KURTTO_V1_URLS_PATCH_RESTORE",
            "PATCH /api/v1/urls/{code}/restore",
            "Reativa URL soft-deleted; exige X-Admin-Secret.",
            PermissionPolicies.KurttoRestore)
    ];

    /// <summary>Metadados das rotas seedadas (política JWT alvo quando o Kurtto validar token).</summary>
    public static IReadOnlyList<KurttoAdminRouteDefinition> KurttoAdminRoutes => AdminRouteDefinitions;

    /// <summary>
    /// Códigos únicos em <see cref="AppRoute.Code"/> para as rotas da API Kurtto sob <c>/api/v1</c>
    /// que exigem credencial (hoje <c>X-Admin-Secret</c> no Kurtto; alinhadas a <c>perm:Kurtto.*</c> quando houver JWT).
    /// </summary>
    public static readonly IReadOnlyList<string> KurttoAuthenticatedRouteCodes =
        AdminRouteDefinitions.Select(d => d.Code).OrderBy(c => c).ToArray();

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

        foreach (var def in AdminRouteDefinitions)
        {
            var exists = await db.Routes.IgnoreQueryFilters()
                .AnyAsync(r => r.Code == def.Code, cancellationToken);
            if (exists)
                continue;

            db.Routes.Add(new AppRoute
            {
                SystemId = systemId.Value,
                Name = def.Name,
                Code = def.Code,
                Description = $"{def.Description} Política alvo: {def.TargetPermissionPolicy}.",
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
