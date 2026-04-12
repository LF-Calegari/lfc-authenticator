using AuthService.Auth;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class KurttoAccessSeederTests : IAsyncLifetime
{
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
    public async Task AfterBootstrap_KurttoSystem_RoutesAndAdminRoleExist()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var systemId = await db.Systems.AsNoTracking()
            .Where(s => s.Code == "kurtto")
            .Select(s => s.Id)
            .SingleAsync();

        var routeCodes = await db.Routes.AsNoTracking()
            .Where(r => r.SystemId == systemId)
            .Select(r => r.Code)
            .OrderBy(c => c)
            .ToListAsync();

        Assert.Equal(KurttoAccessSeeder.KurttoAuthenticatedRouteCodes.OrderBy(c => c), routeCodes);

        var permCount = await db.Permissions.AsNoTracking()
            .CountAsync(p => p.SystemId == systemId);
        Assert.Equal(5, permCount);

        var roleId = await db.Roles.AsNoTracking()
            .Where(r => r.Code == KurttoAccessSeeder.KurttoAdminRoleCode)
            .Select(r => r.Id)
            .SingleAsync();

        var rpCount = await db.RolePermissions.AsNoTracking()
            .CountAsync(rp => rp.RoleId == roleId);
        Assert.Equal(5, rpCount);
    }

    [Fact]
    public async Task EnsureKurttoAccessAsync_Idempotent_DoesNotDuplicateRoutesOrRolePermissions()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await KurttoAccessSeeder.EnsureKurttoAccessAsync(db);
            await KurttoAccessSeeder.EnsureKurttoAccessAsync(db);
        }

        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var systemId = await db2.Systems.AsNoTracking()
            .Where(s => s.Code == "kurtto")
            .Select(s => s.Id)
            .SingleAsync();

        var routes = await db2.Routes.IgnoreQueryFilters()
            .CountAsync(r => r.SystemId == systemId);

        Assert.Equal(KurttoAccessSeeder.KurttoAuthenticatedRouteCodes.Count, routes);

        var roleId = await db2.Roles.IgnoreQueryFilters()
            .Where(r => r.Code == KurttoAccessSeeder.KurttoAdminRoleCode)
            .Select(r => r.Id)
            .SingleAsync();

        var distinctRp = await db2.RolePermissions.IgnoreQueryFilters()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .Distinct()
            .CountAsync();

        Assert.Equal(5, distinctRp);
    }

    [Fact]
    public void Contract_KurttoAuthenticatedRouteCodes_AreDerivedFromAdminRouteDefinitions()
    {
        var expected = KurttoAccessSeeder.KurttoAdminRoutes.Select(r => r.Code).OrderBy(c => c);
        Assert.Equal(expected, KurttoAccessSeeder.KurttoAuthenticatedRouteCodes);
    }

    [Fact]
    public void Contract_KurttoAdminRoutes_MapToExpectedPolicies_ForJwtAlignment()
    {
        var map = KurttoAccessSeeder.KurttoAdminRoutes.ToDictionary(r => r.Code, r => r.TargetPermissionPolicy);
        Assert.Equal(PermissionPolicies.KurttoRead, map["KURTTO_V1_URLS_LIST_INCLUDE_DELETED"]);
        Assert.Equal(PermissionPolicies.KurttoRead, map["KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED"]);
        Assert.Equal(PermissionPolicies.KurttoRestore, map["KURTTO_V1_URLS_PATCH_RESTORE"]);
    }

    [Fact]
    public async Task EnsureKurttoAccessAsync_WhenKurttoSystemMissing_ThrowsInvalidOperationException()
    {
        var baseConnection = Environment.GetEnvironmentVariable("AUTH_SERVICE_TEST_SQL_BASE")?.Trim();
        Assert.False(string.IsNullOrEmpty(baseConnection));

        var databaseName = "auth_svc_kurtto_seed_err_" + Guid.NewGuid().ToString("N");
        var masterConnectionString = SqlServerTestDb.BuildMasterConnectionString(baseConnection);
        var appConnectionString = SqlServerTestDb.BuildDatabaseConnectionString(baseConnection, databaseName);

        SqlServerTestDb.CreateDatabase(masterConnectionString, databaseName);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(appConnectionString)
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                KurttoAccessSeeder.EnsureKurttoAccessAsync(db));

            Assert.Contains("kurtto", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqlServerTestDb.DropDatabase(masterConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task EnsureKurttoAccessAsync_WhenKurttoPermissionsMissing_ThrowsInvalidOperationException()
    {
        var baseConnection = Environment.GetEnvironmentVariable("AUTH_SERVICE_TEST_SQL_BASE")?.Trim();
        Assert.False(string.IsNullOrEmpty(baseConnection));

        var databaseName = "auth_svc_kurtto_seed_noperm_" + Guid.NewGuid().ToString("N");
        var masterConnectionString = SqlServerTestDb.BuildMasterConnectionString(baseConnection);
        var appConnectionString = SqlServerTestDb.BuildDatabaseConnectionString(baseConnection, databaseName);

        SqlServerTestDb.CreateDatabase(masterConnectionString, databaseName);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(appConnectionString)
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            await OfficialCatalogSeeder.EnsureCatalogAsync(db);

            var kurttoSystemId = await db.Systems.AsNoTracking()
                .Where(s => s.Code == "kurtto")
                .Select(s => s.Id)
                .SingleAsync();

            var kurttoPerms = await db.Permissions.Where(p => p.SystemId == kurttoSystemId).ToListAsync();
            db.Permissions.RemoveRange(kurttoPerms);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                KurttoAccessSeeder.EnsureKurttoAccessAsync(db));

            Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqlServerTestDb.DropDatabase(masterConnectionString, databaseName);
        }
    }
}
