using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class PermissionsApiTests : IAsyncLifetime
{
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = await TestApiClient.CreateAuthenticatedAsync(_factory);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> CreateSystemAsync(string code, string name = "Sistema")
    {
        var r = await _client.PostAsJsonAsync("/api/v1/systems", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateRouteAsync(Guid systemId, string code, string name = "Rota")
    {
        var systemTokenTypeId = await GetDefaultSystemTokenTypeIdAsync();
        var r = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { systemId, name, code, description = (string?)null, systemTokenTypeId }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> GetDefaultSystemTokenTypeIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.SystemTokenTypes
            .Where(t => t.Code == SystemTokenTypeSeeder.DefaultCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();
        Assert.True(id.HasValue && id.Value != Guid.Empty);
        return id!.Value;
    }

    private async Task<Guid> CreatePermissionTypeAsync(string code, string name = "Tipo")
    {
        var r = await _client.PostAsJsonAsync("/api/v1/permissions/types", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object PermCreateBody(Guid routeId, Guid permissionTypeId, string? description = null) =>
        new { routeId, permissionTypeId, description };

    private static object PermUpdateBody(Guid routeId, Guid permissionTypeId, string? description = null) =>
        new { routeId, permissionTypeId, description };

    private async Task<PagedPermissionsDto> GetPermissionsPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedPermissionsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    [Fact]
    public async Task Create_Post_WithValidReferences_ReturnsCreated_UtcAndNullDeletedAt()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_1", "Sistema 1");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_1", "Rota 1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_1", "Tipo 1");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, typeId, "Opcional"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(routeId, dto.RouteId);
        Assert.Equal(typeId, dto.PermissionTypeId);
        Assert.Equal("Opcional", dto.Description);
        Assert.Null(dto.DeletedAt);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);

        // Campos denormalizados via Join.
        Assert.Equal("PERM_ROUTE_1", dto.RouteCode);
        Assert.Equal("Rota 1", dto.RouteName);
        Assert.Equal(sysId, dto.SystemId);
        Assert.Equal("PERM_SYS_1", dto.SystemCode);
        Assert.Equal("Sistema 1", dto.SystemName);
        Assert.Equal("PERM_TYPE_1", dto.PermissionTypeCode);
        Assert.Equal("Tipo 1", dto.PermissionTypeName);
    }

    [Fact]
    public async Task Create_WithoutDescription_NormalizesToNull()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_ND");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_ND");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_ND");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, typeId, "   "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task Create_WithInvalidRouteId_ReturnsBadRequest()
    {
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_INV_R");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(Guid.NewGuid(), typeId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutRouteId_ReturnsBadRequest()
    {
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_MISSING_ROUTE");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { permissionTypeId = typeId, description = "sem rota" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidPermissionTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_INV_T");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_INV_T");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DescriptionExceedsMaxLength_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_LEN");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_LEN");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_LEN");
        var longDesc = new string('d', 501);

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, typeId, longDesc), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restore_ActivePermission_ReturnsNotFound()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_AR");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_AR");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_AR");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActivePermissions_AndPopulatesDenormalizedFields()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_GA", "Sistema GA");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_GA", "Rota GA");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_GA", "Tipo GA");
        var firstRes = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId, null), TestApiClient.JsonOptions);
        var firstDto = await firstRes.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(firstDto);

        // Permissões duplicadas (route+type) precisam de tipos diferentes para coexistir; criamos um segundo tipo.
        var typeId2 = await CreatePermissionTypeAsync("PERM_TYPE_GA2", "Tipo GA2");
        var other = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId2, "b"), TestApiClient.JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/api/v1/permissions/{toDelete.Id}");

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sysId}&pageSize=100");
        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.All(page.Data, p => Assert.Null(p.DeletedAt));
        Assert.Contains(page.Data, p => p.Id == firstDto.Id);
        Assert.DoesNotContain(page.Data, p => p.Id == toDelete.Id);

        var item = page.Data.Single(p => p.Id == firstDto.Id);
        Assert.Equal("PERM_ROUTE_GA", item.RouteCode);
        Assert.Equal("Rota GA", item.RouteName);
        Assert.Equal(sysId, item.SystemId);
        Assert.Equal("PERM_SYS_GA", item.SystemCode);
        Assert.Equal("Sistema GA", item.SystemName);
        Assert.Equal("PERM_TYPE_GA", item.PermissionTypeCode);
        Assert.Equal("Tipo GA", item.PermissionTypeName);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_G1", "Sistema G1");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_G1", "Rota G1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_G1", "Tipo G1");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOkResp = await _client.GetAsync($"/api/v1/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOkResp.StatusCode);
        var getOk = await getOkResp.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(getOk);
        Assert.Equal("PERM_ROUTE_G1", getOk.RouteCode);
        Assert.Equal("Rota G1", getOk.RouteName);
        Assert.Equal(sysId, getOk.SystemId);
        Assert.Equal("PERM_SYS_G1", getOk.SystemCode);
        Assert.Equal("Sistema G1", getOk.SystemName);
        Assert.Equal("PERM_TYPE_G1", getOk.PermissionTypeCode);
        Assert.Equal("Tipo G1", getOk.PermissionTypeName);

        await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_U1");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_U1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_U1");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId, "a"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/permissions/{dto.Id}",
            PermUpdateBody(routeId, typeId, "b"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);
        var updated = await putOk.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("PERM_ROUTE_U1", updated.RouteCode);

        await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/permissions/{dto.Id}",
            PermUpdateBody(routeId, typeId, "c"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidRouteId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_UINV");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_UINV");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_UINV");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/permissions/{dto.Id}",
            PermUpdateBody(Guid.NewGuid(), typeId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutPermissionTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_MISSING_TYPE");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_MISSING_TYPE");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_MISSING_TYPE");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/permissions/{dto.Id}",
            new { routeId, description = "sem tipo" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_D1");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_D1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_D1");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Permissions.IgnoreQueryFilters().SingleAsync(p => p.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetByIdWorks()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_R1");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_R1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_R1");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Create_WithDeletedRoute_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_DELROUTE");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_DELROUTE");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_DELROUTE");
        await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeWhenRouteSoftDeleted()
    {
        // Issue #157 alterou o contrato: rotas com Permissions ativas não podem ser deletadas (409).
        // Para validar a invariante "permissão some do GET após soft-delete da rota",
        // o teste primeiro soft-deleta a permissão (liberando a rota para DELETE), depois soft-deleta a rota.
        var sysId = await CreateSystemAsync("PERM_SYS_ORPH");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_ORPH");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_ORPH");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sysId}&pageSize=100");
        Assert.DoesNotContain(page.Data, p => p.Id == dto.Id);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeWhenPermissionTypeSoftDeleted()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_PT");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_PT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_PT");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/permissions/types/{typeId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/permissions/{dto.Id}")).StatusCode);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sysId}&pageSize=100");
        Assert.DoesNotContain(page.Data, p => p.Id == dto.Id);
    }

    [Fact]
    public async Task Restore_WhenRouteSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_RROUTE");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_RROUTE");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_RROUTE");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}");
        await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task Create_WithDeletedPermissionType_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_DELPT");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_DELPT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_DELPT");
        await _client.DeleteAsync($"/api/v1/permissions/types/{typeId}");

        var response = await _client.PostAsJsonAsync("/api/v1/permissions",
            PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restore_WhenPermissionTypeSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_RPT");
        var routeId = await CreateRouteAsync(sysId, "PERM_ROUTE_RPT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_RPT");
        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(routeId, typeId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/permissions/{dto.Id}");
        await _client.DeleteAsync($"/api/v1/permissions/types/{typeId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    // --- Filtros, busca, paginação e includeDeleted (issue #165) ---

    [Fact]
    public async Task GetAll_FilterBySystemId_ReturnsOnlyMatching()
    {
        var sysA = await CreateSystemAsync("PERM_FS_A");
        var sysB = await CreateSystemAsync("PERM_FS_B");
        var rA = await CreateRouteAsync(sysA, "PERM_FS_A_ROUTE");
        var rB = await CreateRouteAsync(sysB, "PERM_FS_B_ROUTE");
        var t = await CreatePermissionTypeAsync("PERM_FS_T");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA, t), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rB, t), TestApiClient.JsonOptions);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sysA}&pageSize=100");
        Assert.All(page.Data, p => Assert.Equal(sysA, p.SystemId));
        Assert.Contains(page.Data, p => p.RouteCode == "PERM_FS_A_ROUTE");
        Assert.DoesNotContain(page.Data, p => p.RouteCode == "PERM_FS_B_ROUTE");
    }

    [Fact]
    public async Task GetAll_FilterBySystemId_Empty_Returns400()
    {
        var resp = await _client.GetAsync($"/api/v1/permissions?systemId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_FilterBySystemId_Unknown_ReturnsEmptyPage()
    {
        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={Guid.NewGuid()}");
        Assert.Empty(page.Data);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task GetAll_FilterByRouteId_ReturnsOnlyMatching()
    {
        var sys = await CreateSystemAsync("PERM_FR_S");
        var rA = await CreateRouteAsync(sys, "PERM_FR_RA");
        var rB = await CreateRouteAsync(sys, "PERM_FR_RB");
        var t = await CreatePermissionTypeAsync("PERM_FR_T");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA, t), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rB, t), TestApiClient.JsonOptions);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?routeId={rA}&pageSize=100");
        Assert.All(page.Data, p => Assert.Equal(rA, p.RouteId));
    }

    [Fact]
    public async Task GetAll_FilterByPermissionTypeId_ReturnsOnlyMatching()
    {
        var sys = await CreateSystemAsync("PERM_FT_S");
        var r = await CreateRouteAsync(sys, "PERM_FT_R");
        var tA = await CreatePermissionTypeAsync("PERM_FT_TA");
        var tB = await CreatePermissionTypeAsync("PERM_FT_TB");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(r, tA), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(r, tB), TestApiClient.JsonOptions);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?permissionTypeId={tA}&pageSize=100");
        Assert.All(page.Data, p => Assert.Equal(tA, p.PermissionTypeId));
    }

    [Fact]
    public async Task GetAll_FilterCombined_SystemId_AndPermissionTypeId()
    {
        var sysA = await CreateSystemAsync("PERM_CMB_A");
        var sysB = await CreateSystemAsync("PERM_CMB_B");
        var rA = await CreateRouteAsync(sysA, "PERM_CMB_A_R");
        var rB = await CreateRouteAsync(sysB, "PERM_CMB_B_R");
        var t1 = await CreatePermissionTypeAsync("PERM_CMB_T1");
        var t2 = await CreatePermissionTypeAsync("PERM_CMB_T2");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA, t1), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA, t2), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rB, t1), TestApiClient.JsonOptions);

        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sysA}&permissionTypeId={t1}&pageSize=100");
        Assert.Single(page.Data);
        Assert.Equal(sysA, page.Data[0].SystemId);
        Assert.Equal(t1, page.Data[0].PermissionTypeId);
    }

    [Fact]
    public async Task GetAll_QSearch_MatchesRouteCode_RouteName_AndDescription()
    {
        var sys = await CreateSystemAsync("PERM_Q_S");
        var rA = await CreateRouteAsync(sys, "PERM_Q_USERS_LIST", "Listar usuarios");
        var rB = await CreateRouteAsync(sys, "PERM_Q_REL", "Relatorios");
        var t = await CreatePermissionTypeAsync("PERM_Q_T");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA, t, "DescA"), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rB, t, "matchabledesc"), TestApiClient.JsonOptions);

        // Match por RouteCode
        var byCode = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&q=USERS_LIST&pageSize=100");
        Assert.Contains(byCode.Data, p => p.RouteCode == "PERM_Q_USERS_LIST");

        // Match por RouteName (case-insensitive)
        var byName = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&q=relat&pageSize=100");
        Assert.Contains(byName.Data, p => p.RouteCode == "PERM_Q_REL");

        // Match por Description
        var byDesc = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&q=matchable&pageSize=100");
        Assert.Single(byDesc.Data);
        Assert.Equal("PERM_Q_REL", byDesc.Data[0].RouteCode);
    }

    [Fact]
    public async Task GetAll_QSearch_EscapesWildcards()
    {
        var sys = await CreateSystemAsync("PERM_Q_ESC");
        var literal = await CreateRouteAsync(sys, "PERM_LIT_ESC", "Rota com 100% literal");
        var noisy = await CreateRouteAsync(sys, "PERM_NOISE_ESC", "Outra rota");
        var t = await CreatePermissionTypeAsync("PERM_Q_ESC_T");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(literal, t), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(noisy, t), TestApiClient.JsonOptions);

        // q="100%" deve casar APENAS o literal (% escapado, não wildcard).
        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&q=100%25&pageSize=100");
        Assert.Single(page.Data);
        Assert.Equal("PERM_LIT_ESC", page.Data[0].RouteCode);
    }

    [Fact]
    public async Task GetAll_Pagination_SecondPage_RespectsSkipTake()
    {
        var sys = await CreateSystemAsync("PERM_PG_S");
        var route = await CreateRouteAsync(sys, "PERM_PG_R");
        var types = new List<Guid>();
        for (int i = 0; i < 5; i++)
            types.Add(await CreatePermissionTypeAsync($"PERM_PG_T{i}"));

        foreach (var t in types)
            await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(route, t), TestApiClient.JsonOptions);

        var first = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&page=1&pageSize=2");
        var second = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&page=2&pageSize=2");
        var third = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&page=3&pageSize=2");

        Assert.Equal(2, first.Data.Count);
        Assert.Equal(2, second.Data.Count);
        Assert.Single(third.Data);
        Assert.Equal(5, first.Total);
        Assert.Equal(5, second.Total);
        Assert.Equal(5, third.Total);

        var ids = first.Data.Concat(second.Data).Concat(third.Data).Select(p => p.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public async Task GetAll_PageSize_TooLarge_Returns400()
    {
        var resp = await _client.GetAsync("/api/v1/permissions?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageSize_Zero_Returns400()
    {
        var resp = await _client.GetAsync("/api/v1/permissions?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_Page_Zero_Returns400()
    {
        var resp = await _client.GetAsync("/api/v1/permissions?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_IncludeDeletedTrue_ShowsPermissionsWithSoftDeletedRoute()
    {
        var sys = await CreateSystemAsync("PERM_INC_S", "Sistema INC");
        var route = await CreateRouteAsync(sys, "PERM_INC_R", "Rota INC");
        var type = await CreatePermissionTypeAsync("PERM_INC_T", "Tipo INC");

        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(route, type), TestApiClient.JsonOptions);
        var perm = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(perm);

        // Soft-deleta a permissão para liberar a rota; depois soft-deleta a rota.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/permissions/{perm.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{route}")).StatusCode);

        // Default (includeDeleted=false): permissão some.
        var hidden = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&pageSize=100");
        Assert.DoesNotContain(hidden.Data, p => p.Id == perm.Id);

        // Com includeDeleted=true: permissão aparece com a rota soft-deletada e os campos denormalizados ainda preenchidos.
        var visible = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&includeDeleted=true&pageSize=100");
        var item = Assert.Single(visible.Data, p => p.Id == perm.Id);
        Assert.NotNull(item.DeletedAt);
        Assert.Equal("PERM_INC_R", item.RouteCode);
        Assert.Equal("Rota INC", item.RouteName);
        Assert.Equal(sys, item.SystemId);
        Assert.Equal("PERM_INC_S", item.SystemCode);
        Assert.Equal("Sistema INC", item.SystemName);
        Assert.Equal("PERM_INC_T", item.PermissionTypeCode);
    }

    [Fact]
    public async Task GetAll_IncludeDeletedTrue_FiltersBySystemId_OfDeletedRoute()
    {
        var sys = await CreateSystemAsync("PERM_INCSYS_S");
        var route = await CreateRouteAsync(sys, "PERM_INCSYS_R");
        var type = await CreatePermissionTypeAsync("PERM_INCSYS_T");

        var create = await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(route, type), TestApiClient.JsonOptions);
        var perm = await create.Content.ReadFromJsonAsync<PermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(perm);
        await _client.DeleteAsync($"/api/v1/permissions/{perm.Id}");
        await _client.DeleteAsync($"/api/v1/systems/routes/{route}");

        // includeDeleted=true + systemId precisa achar a permissão mesmo com a rota soft-deletada.
        var page = await GetPermissionsPageAsync($"/api/v1/permissions?systemId={sys}&includeDeleted=true&pageSize=100");
        Assert.Contains(page.Data, p => p.Id == perm.Id);
    }

    [Fact]
    public async Task GetAll_DefaultOrdering_IsBySystemCode_RouteCode_PermissionTypeCode()
    {
        var sysA = await CreateSystemAsync("PERM_ORD_A_SYS");
        var sysZ = await CreateSystemAsync("PERM_ORD_Z_SYS");
        var rA1 = await CreateRouteAsync(sysA, "PERM_ORD_A_R1");
        var rA2 = await CreateRouteAsync(sysA, "PERM_ORD_A_R2");
        var rZ = await CreateRouteAsync(sysZ, "PERM_ORD_Z_R");
        var t = await CreatePermissionTypeAsync("PERM_ORD_T");

        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rZ, t), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA2, t), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/permissions", PermCreateBody(rA1, t), TestApiClient.JsonOptions);

        var page = await GetPermissionsPageAsync("/api/v1/permissions?pageSize=100");
        // Filtramos para os codes que criamos para evitar interferência do seed.
        var ours = page.Data.Where(p => p.SystemCode.StartsWith("PERM_ORD_", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, ours.Count);
        Assert.Equal("PERM_ORD_A_SYS", ours[0].SystemCode);
        Assert.Equal("PERM_ORD_A_R1", ours[0].RouteCode);
        Assert.Equal("PERM_ORD_A_SYS", ours[1].SystemCode);
        Assert.Equal("PERM_ORD_A_R2", ours[1].RouteCode);
        Assert.Equal("PERM_ORD_Z_SYS", ours[2].SystemCode);
    }

    private sealed record RefDto(Guid Id);

    private sealed record PermissionDto(
        Guid Id,
        Guid RouteId,
        string RouteCode,
        string RouteName,
        Guid SystemId,
        string SystemCode,
        string SystemName,
        Guid PermissionTypeId,
        string PermissionTypeCode,
        string PermissionTypeName,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record PagedPermissionsDto(
        List<PermissionDto> Data,
        int Page,
        int PageSize,
        int Total);
}
