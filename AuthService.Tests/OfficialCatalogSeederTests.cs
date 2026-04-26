using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

/// <summary>
/// Valida o estado final do catálogo oficial após o bootstrap dos seeds: sistemas,
/// tipos de permissão e o produto cartesiano de permissões esperado, além da idempotência
/// de re-execução. Cobre os critérios de aceite da issue #143 (catálogo cobre
/// <c>authenticator</c> + <c>kurtto</c> com todos os tipos CRUD/Restore).
/// </summary>
public class OfficialCatalogSeederTests : IAsyncLifetime
{
    private static readonly string[] ExpectedSystemCodes = { "authenticator", "kurtto" };

    private static readonly string[] ExpectedPermissionTypeCodes =
        { "create", "read", "update", "delete", "restore" };

    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AfterBootstrap_AllExpectedSystemsExist()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var systemCodes = await db.Systems.AsNoTracking()
            .Select(s => s.Code)
            .OrderBy(c => c)
            .ToListAsync();

        foreach (var expected in ExpectedSystemCodes)
            Assert.Contains(expected, systemCodes);
    }

    [Fact]
    public async Task AfterBootstrap_AllExpectedPermissionTypesExist()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var typeCodes = await db.PermissionTypes.AsNoTracking()
            .Select(t => t.Code)
            .OrderBy(c => c)
            .ToListAsync();

        foreach (var expected in ExpectedPermissionTypeCodes)
            Assert.Contains(expected, typeCodes);
    }

    [Fact]
    public async Task AfterBootstrap_PermissionsCoverFullCartesianProductForExpectedSystems()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairs = await db.Permissions.AsNoTracking()
            .Join(
                db.Systems.AsNoTracking(),
                p => p.SystemId,
                s => s.Id,
                (p, s) => new { p.PermissionTypeId, SystemCode = s.Code })
            .Join(
                db.PermissionTypes.AsNoTracking(),
                x => x.PermissionTypeId,
                t => t.Id,
                (x, t) => new { x.SystemCode, TypeCode = t.Code })
            .ToListAsync();

        foreach (var systemCode in ExpectedSystemCodes)
        {
            foreach (var typeCode in ExpectedPermissionTypeCodes)
            {
                Assert.Contains(
                    pairs,
                    p => p.SystemCode == systemCode && p.TypeCode == typeCode);
            }
        }
    }

    [Fact]
    public async Task EnsureCatalogAsync_Idempotent_DoesNotDuplicateSystemsTypesOrPermissions()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await OfficialCatalogSeeder.EnsureCatalogAsync(db);
            await OfficialCatalogSeeder.EnsureCatalogAsync(db);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var systemCode in ExpectedSystemCodes)
        {
            var systemCount = await verifyDb.Systems.IgnoreQueryFilters()
                .CountAsync(s => s.Code == systemCode);
            Assert.Equal(1, systemCount);
        }

        foreach (var typeCode in ExpectedPermissionTypeCodes)
        {
            var typeCount = await verifyDb.PermissionTypes.IgnoreQueryFilters()
                .CountAsync(t => t.Code == typeCode);
            Assert.Equal(1, typeCount);
        }

        foreach (var systemCode in ExpectedSystemCodes)
        {
            var systemId = await verifyDb.Systems.AsNoTracking()
                .Where(s => s.Code == systemCode)
                .Select(s => s.Id)
                .SingleAsync();

            foreach (var typeCode in ExpectedPermissionTypeCodes)
            {
                var typeId = await verifyDb.PermissionTypes.AsNoTracking()
                    .Where(t => t.Code == typeCode)
                    .Select(t => t.Id)
                    .SingleAsync();

                var permCount = await verifyDb.Permissions.IgnoreQueryFilters()
                    .CountAsync(p => p.SystemId == systemId && p.PermissionTypeId == typeId);
                Assert.Equal(1, permCount);
            }
        }
    }
}
