using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RoutesApiTests : IAsyncLifetime
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
        var dto = await r.Content.ReadFromJsonAsync<SystemRefDto>(TestApiClient.JsonOptions);
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

    private async Task<Guid> CreateActiveSystemTokenTypeAsync(string code, string name = "Custom")
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/tokens/types", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<TokenTypeRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object RouteCreateBody(Guid systemId, string name, string code, Guid systemTokenTypeId, string? description = null) =>
        new { systemId, name, code, description, systemTokenTypeId };

    private static object RouteUpdateBody(Guid systemId, string name, string code, Guid systemTokenTypeId, string? description = null) =>
        new { systemId, name, code, description, systemTokenTypeId };

    [Fact]
    public async Task Create_Post_WithValidSystemId_ReturnsCreated_UtcAndNullDeletedAt()
    {
        var sysId = await CreateSystemAsync("RT_SYS_1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota A", "ROUTE_A", sttId, "Opcional"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(sysId, dto.SystemId);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("ROUTE_A", dto.Code);
        Assert.Equal("Opcional", dto.Description);
        Assert.Equal(sttId, dto.SystemTokenTypeId);
        Assert.Equal(SystemTokenTypeSeeder.DefaultCode, dto.SystemTokenTypeCode);
        Assert.False(string.IsNullOrWhiteSpace(dto.SystemTokenTypeName));
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(Guid.NewGuid(), "X", "CODE_X", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutSystemId_ReturnsBadRequest()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { name = "Rota sem sistema", code = "RT_NO_SYS", systemTokenTypeId = sttId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_NO_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { systemId = sysId, name = "Rota sem stt", code = "RT_NO_STT" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptySystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_EMPTY_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota empty stt", "RT_EMPTY_STT", Guid.Empty), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_INV_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota inv stt", "RT_INV_STT", Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInactiveSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DEL_STT");
        var customId = await CreateActiveSystemTokenTypeAsync("STT_DEL_FOR_RT");

        var del = await _client.DeleteAsync($"/api/v1/tokens/types/{customId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota stt deletado", "RT_DEL_STT", customId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DUP");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", "DUP_CODE", sttId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "B", "DUP_CODE", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", "ABC_RT", sttId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "B", "  ABC_RT  ", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_CONC");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        const string code = "CONC_RT";
        var t1 = _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", code, sttId), TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "B", code, sttId), TestApiClient.JsonOptions);
        await Task.WhenAll(t1, t2);
        var statuses = new[] { (await t1).StatusCode, (await t2).StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WN");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "   ", "C1", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoutes_AndPopulatesDenormalizedTokenTypeFields()
    {
        var sysId = await CreateSystemAsync("RT_SYS_GA");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "Ativa", "RT_ATIVA", sttId), TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "Outra", "RT_OUTRA", sttId), TestApiClient.JsonOptions);
        var otherDto = await other.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(otherDto);
        await _client.DeleteAsync($"/api/v1/systems/routes/{otherDto.Id}");

        var page = await GetRoutesPageAsync("/api/v1/systems/routes?pageSize=100");
        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.All(page.Data, r => Assert.Null(r.DeletedAt));
        Assert.All(page.Data, r => Assert.False(string.IsNullOrWhiteSpace(r.SystemTokenTypeCode)));
        Assert.All(page.Data, r => Assert.False(string.IsNullOrWhiteSpace(r.SystemTokenTypeName)));
        Assert.Contains(page.Data, r => r.Code == "RT_ATIVA");
        Assert.DoesNotContain(page.Data, r => r.Code == "RT_OUTRA");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_G1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_G1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOkResp = await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOkResp.StatusCode);
        var getOk = await getOkResp.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(getOk);
        Assert.Equal(sttId, getOk.SystemTokenTypeId);
        Assert.Equal(SystemTokenTypeSeeder.DefaultCode, getOk.SystemTokenTypeCode);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_U1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_U1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_U1", sttId, null), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S3", "RT_U1", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UINV");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_INV", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_INV", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_MISSING");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_MISSING", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            new { name = "S atualizado", code = "RT_MISSING", systemTokenTypeId = sttId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_MISSING_STT");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_MISS_STT", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            new { systemId = sysId, name = "S2", code = "RT_MISS_STT" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithUnknownSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UPD_INV_STT");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_UPD_INV_STT", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_UPD_INV_STT", Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithInactiveSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UPD_DEL_STT");
        var defaultSttId = await GetDefaultSystemTokenTypeIdAsync();
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_UPD_DEL");

        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_UPD_DEL_STT", defaultSttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del = await _client.DeleteAsync($"/api/v1/tokens/types/{customSttId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_UPD_DEL_STT", customSttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentId_WithInvalidSystemId_Returns404()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var missingId = Guid.NewGuid();
        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{missingId}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_NO404", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_D1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_D1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Routes.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutLinkedPermissions_ReturnsNoContent()
    {
        // Issue #157: rota sem Permissions vinculadas mantém o comportamento legado (204).
        var sysId = await CreateSystemAsync("RT_SYS_LP_NONE");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_NONE", sttId);

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        await AssertRouteSoftDeletedAsync(routeId, expectDeleted: true);
    }

    [Fact]
    public async Task Delete_WithOneActiveLinkedPermission_ReturnsConflictAndDoesNotMutate()
    {
        // Issue #157: 1 Permission ativa bloqueia o DELETE com 409 e linkedPermissionsCount=1.
        var sysId = await CreateSystemAsync("RT_SYS_LP_ONE");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_ONE", sttId);
        var typeId = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_ONE_TYPE");

        var perm = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeId, description = (string?)null }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, perm.StatusCode);
        var permDto = await perm.Content.ReadFromJsonAsync<PermissionRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(permDto);

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);

        var payload = await del.Content.ReadFromJsonAsync<DeleteConflictDto>(TestApiClient.JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.LinkedPermissionsCount);
        Assert.False(string.IsNullOrWhiteSpace(payload.Message));
        Assert.Contains("permissões ativas vinculadas", payload.Message);

        // Caminho 409: rota não soft-deletada e permissão preservada.
        await AssertRouteSoftDeletedAsync(routeId, expectDeleted: false);
        await AssertPermissionDeletedAtAsync(permDto.Id, expectDeleted: false);
    }

    [Fact]
    public async Task Delete_WithThreeActiveLinkedPermissions_ReturnsConflictWithCountThree()
    {
        // Issue #157: linkedPermissionsCount reflete a contagem exata de Permissions ativas.
        var sysId = await CreateSystemAsync("RT_SYS_LP_THREE");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_THREE", sttId);

        for (var i = 0; i < 3; i++)
        {
            var typeId = await CreatePermissionTypeForLinkedPermissionsAsync($"RT_LP_THREE_T{i}");
            var perm = await _client.PostAsJsonAsync("/api/v1/permissions",
                new { routeId, permissionTypeId = typeId, description = (string?)null }, TestApiClient.JsonOptions);
            Assert.Equal(HttpStatusCode.Created, perm.StatusCode);
        }

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        var payload = await del.Content.ReadFromJsonAsync<DeleteConflictDto>(TestApiClient.JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(3, payload.LinkedPermissionsCount);

        await AssertRouteSoftDeletedAsync(routeId, expectDeleted: false);
    }

    [Fact]
    public async Task Delete_WithOnlySoftDeletedLinkedPermissions_ReturnsNoContent()
    {
        // Issue #157: Permissions soft-deletadas não bloqueiam (alinhado ao filtro global do EF).
        var sysId = await CreateSystemAsync("RT_SYS_LP_SOFT");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_SOFT", sttId);
        var typeId = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_SOFT_TYPE");

        var perm = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeId, description = (string?)null }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, perm.StatusCode);
        var permDto = await perm.Content.ReadFromJsonAsync<PermissionRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(permDto);

        var deletePerm = await _client.DeleteAsync($"/api/v1/permissions/{permDto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletePerm.StatusCode);
        await AssertPermissionDeletedAtAsync(permDto.Id, expectDeleted: true);

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        await AssertRouteSoftDeletedAsync(routeId, expectDeleted: true);
    }

    [Fact]
    public async Task Delete_WithMixedActiveAndSoftDeletedPermissions_ReturnsConflictWithActiveCountOnly()
    {
        // Issue #157: contagem ignora permissões soft-deletadas e foca apenas nas ativas.
        var sysId = await CreateSystemAsync("RT_SYS_LP_MIX");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_MIX", sttId);

        var typeIdA = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_MIX_TA");
        var typeIdB = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_MIX_TB");
        var typeIdC = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_MIX_TC");

        var permA = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeIdA, description = (string?)null }, TestApiClient.JsonOptions);
        var permB = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeIdB, description = (string?)null }, TestApiClient.JsonOptions);
        var permC = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeIdC, description = (string?)null }, TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, permA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, permB.StatusCode);
        Assert.Equal(HttpStatusCode.Created, permC.StatusCode);

        var permADto = await permA.Content.ReadFromJsonAsync<PermissionRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(permADto);

        // Soft-delete da Permission A — restam B e C ativas vinculadas à rota.
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"/api/v1/permissions/{permADto.Id}")).StatusCode);

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        var payload = await del.Content.ReadFromJsonAsync<DeleteConflictDto>(TestApiClient.JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(2, payload.LinkedPermissionsCount);

        await AssertRouteSoftDeletedAsync(routeId, expectDeleted: false);
    }

    [Fact]
    public async Task Delete_BlockedByPermissions_DoesNotPersistAnySideEffect()
    {
        // Issue #157: contrato de "no side-effect" no caminho 409 — UpdatedAt da rota e DeletedAt
        // da permissão devem permanecer idênticos aos valores de antes da chamada bloqueada.
        var sysId = await CreateSystemAsync("RT_SYS_LP_NOSIDE");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var routeId = await CreateRouteForLinkedPermissionsAsync(sysId, "S", "RT_LP_NOSIDE", sttId);
        var typeId = await CreatePermissionTypeForLinkedPermissionsAsync("RT_LP_NOSIDE_TYPE");

        var perm = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId = typeId, description = (string?)null }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, perm.StatusCode);
        var permDto = await perm.Content.ReadFromJsonAsync<PermissionRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(permDto);

        var (routeUpdatedBefore, routeDeletedBefore) = await GetRouteTimestampsAsync(routeId);
        var (permUpdatedBefore, permDeletedBefore) = await GetPermissionTimestampsAsync(permDto.Id);

        var del = await _client.DeleteAsync($"/api/v1/systems/routes/{routeId}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);

        var (routeUpdatedAfter, routeDeletedAfter) = await GetRouteTimestampsAsync(routeId);
        var (permUpdatedAfter, permDeletedAfter) = await GetPermissionTimestampsAsync(permDto.Id);

        Assert.Equal(routeUpdatedBefore, routeUpdatedAfter);
        Assert.Equal(routeDeletedBefore, routeDeletedAfter);
        Assert.Null(routeDeletedAfter);

        Assert.Equal(permUpdatedBefore, permUpdatedAfter);
        Assert.Equal(permDeletedBefore, permDeletedAfter);
        Assert.Null(permDeletedAfter);
    }

    private async Task<Guid> CreateRouteForLinkedPermissionsAsync(Guid systemId, string name, string code, Guid systemTokenTypeId)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(systemId, name, code, systemTokenTypeId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionTypeForLinkedPermissionsAsync(string code)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/permissions/types",
            new { name = code, code, description = (string?)null }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<PermissionTypeRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task AssertRouteSoftDeletedAsync(Guid routeId, bool expectDeleted)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Routes.IgnoreQueryFilters().SingleAsync(r => r.Id == routeId);
        if (expectDeleted)
            Assert.NotNull(row.DeletedAt);
        else
            Assert.Null(row.DeletedAt);
    }

    private async Task AssertPermissionDeletedAtAsync(Guid permissionId, bool expectDeleted)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Permissions.IgnoreQueryFilters().SingleAsync(p => p.Id == permissionId);
        if (expectDeleted)
            Assert.NotNull(row.DeletedAt);
        else
            Assert.Null(row.DeletedAt);
    }

    private async Task<(DateTime UpdatedAt, DateTime? DeletedAt)> GetRouteTimestampsAsync(Guid routeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Routes.IgnoreQueryFilters().SingleAsync(r => r.Id == routeId);
        return (row.UpdatedAt, row.DeletedAt);
    }

    private async Task<(DateTime UpdatedAt, DateTime? DeletedAt)> GetPermissionTimestampsAsync(Guid permissionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Permissions.IgnoreQueryFilters().SingleAsync(p => p.Id == permissionId);
        return (row.UpdatedAt, row.DeletedAt);
    }

    [Fact]
    public async Task Restore_Deleted_ThenAppearsInGetAll_AndGetByIdWorks()
    {
        var sysId = await CreateSystemAsync("RT_SYS_R1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_R1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);

        var page = await GetRoutesPageAsync("/api/v1/systems/routes?pageSize=100");
        Assert.Contains(page.Data, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Create_WithDeletedSystem_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DELSYS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "R", "RT_DELSYS", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeRoutesWhenSystemSoftDeleted()
    {
        var sysId = await CreateSystemAsync("RT_SYS_ORPH");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "R", "RT_ORPH", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        var page = await GetRoutesPageAsync("/api/v1/systems/routes?pageSize=100");
        Assert.DoesNotContain(page.Data, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Restore_WhenSystemSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_RSYS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "S", "RT_RSYS", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    [Fact]
    public async Task Restore_WhenSystemTokenTypeSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_RSTT");
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_RSTT");

        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "S", "RT_RSTT", customSttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        // soft-delete da rota
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
        // soft-delete do SystemTokenType referenciado
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/tokens/types/{customSttId}")).StatusCode);

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    [Fact]
    public async Task Sync_WithoutSystemTokenTypeCode_DefaultsToCanonicalDefault()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_DEF", "Sistema sync default");
        var defaultSttId = await GetDefaultSystemTokenTypeIdAsync();

        var body = new
        {
            systemCode = "RT_SYNC_DEF",
            routes = new[]
            {
                new { code = "SYNC_DEF_RT", name = "Rota sync default", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = (string?)null }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.SingleAsync(r => r.Code == "SYNC_DEF_RT");
        Assert.Equal(defaultSttId, route.SystemTokenTypeId);
    }

    [Fact]
    public async Task Sync_WithUnknownSystemTokenTypeCode_ReturnsBadRequest_AndListsUnknownCodes()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_UNK_STT", "Sistema sync stt unknown");

        var body = new
        {
            systemCode = "RT_SYNC_UNK_STT",
            routes = new[]
            {
                new { code = "SYNC_UNK_STT_RT", name = "Rota stt unknown", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = "stt-nope-1" }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.Contains("stt-nope-1", content);
    }

    [Fact]
    public async Task Sync_WithKnownSystemTokenTypeCode_PersistsOnRoute()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_KNOWN_STT", "Sistema sync stt known");
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_SYNC_KNOWN");

        var body = new
        {
            systemCode = "RT_SYNC_KNOWN_STT",
            routes = new[]
            {
                new { code = "SYNC_KNOWN_STT_RT", name = "Rota stt known", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = "STT_SYNC_KNOWN" }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.SingleAsync(r => r.Code == "SYNC_KNOWN_STT_RT");
        Assert.Equal(customSttId, route.SystemTokenTypeId);
    }

    [Fact]
    public async Task GetAll_NoFilters_UsesDefaultEnvelope()
    {
        var page = await GetRoutesPageAsync("/api/v1/systems/routes");
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.Total >= page.Data.Count);
    }

    [Fact]
    public async Task GetAll_WithSystemIdEmpty_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync($"/api/v1/systems/routes?systemId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithSystemIdUnknown_ReturnsOkAndEmptyData()
    {
        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={Guid.NewGuid()}");
        Assert.Empty(page.Data);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task GetAll_WithSystemIdFilter_ReturnsOnlyRoutesOfThatSystem()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysA = await CreateSystemAsync("RT_FILT_SYS_A", "Sistema A");
        var sysB = await CreateSystemAsync("RT_FILT_SYS_B", "Sistema B");

        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysA, "Rota A1", "RT_FILT_A1", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysA, "Rota A2", "RT_FILT_A2", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysB, "Rota B1", "RT_FILT_B1", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysA}&pageSize=100");
        Assert.All(page.Data, r => Assert.Equal(sysA, r.SystemId));
        Assert.Contains(page.Data, r => r.Code == "RT_FILT_A1");
        Assert.Contains(page.Data, r => r.Code == "RT_FILT_A2");
        Assert.DoesNotContain(page.Data, r => r.Code == "RT_FILT_B1");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnCode()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_Q_CODE_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Listar usuarios", "USERS_LIST", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Outra", "OUTRO_RT", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync("/api/v1/systems/routes?q=USERS&pageSize=100");
        Assert.Contains(page.Data, r => r.Code == "USERS_LIST");
        Assert.DoesNotContain(page.Data, r => r.Code == "OUTRO_RT");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnName()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_Q_NAME_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Pagina de relatorios", "REL_PAGE", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Outra", "DIF_PAGE", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync("/api/v1/systems/routes?q=relat&pageSize=100");
        Assert.Contains(page.Data, r => r.Code == "REL_PAGE");
        Assert.DoesNotContain(page.Data, r => r.Code == "DIF_PAGE");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_IsCaseInsensitive()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_Q_CI_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Admin Page", "ADMIN_PAGE_RT", sttId), TestApiClient.JsonOptions);

        var lower = await GetRoutesPageAsync("/api/v1/systems/routes?q=admin&pageSize=100");
        var upper = await GetRoutesPageAsync("/api/v1/systems/routes?q=ADMIN&pageSize=100");
        var mixed = await GetRoutesPageAsync("/api/v1/systems/routes?q=AdMiN&pageSize=100");

        Assert.Contains(lower.Data, r => r.Code == "ADMIN_PAGE_RT");
        Assert.Contains(upper.Data, r => r.Code == "ADMIN_PAGE_RT");
        Assert.Contains(mixed.Data, r => r.Code == "ADMIN_PAGE_RT");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_EscapesUnderscoreLiteral()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_Q_ESC_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Liter A", "ESC_LIT_AAA", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Liter B", "ESCXLITXBBB", sttId), TestApiClient.JsonOptions);

        // O usuario busca pela substring "_LIT_" literal. Sem o escape, "_" viraria wildcard
        // ILIKE e casaria tambem o code "ESCXLITXBBB" (qualquer caractere no lugar dos "_").
        // Com o escape correto, somente o code que contem "_LIT_" literalmente deve aparecer.
        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&q=_LIT_&pageSize=100");
        Assert.Contains(page.Data, r => r.Code == "ESC_LIT_AAA");
        Assert.DoesNotContain(page.Data, r => r.Code == "ESCXLITXBBB");
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsCorrectSubset()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_PAGE_SYS");
        for (var i = 1; i <= 5; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/systems/routes",
                RouteCreateBody(sysId, $"Rota {i:D2}", $"PG_RT_{i:D2}", sttId), TestApiClient.JsonOptions);
        }

        var first = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=1&pageSize=2");
        var second = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=2&pageSize=2");
        var third = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=3&pageSize=2");

        Assert.Equal(2, first.Data.Count);
        Assert.Equal(2, second.Data.Count);
        Assert.Single(third.Data);
        Assert.Equal(5, first.Total);
        Assert.Equal(5, second.Total);
        Assert.Equal(5, third.Total);

        var firstCodes = first.Data.Select(s => s.Code).ToList();
        var secondCodes = second.Data.Select(s => s.Code).ToList();
        var thirdCodes = third.Data.Select(s => s.Code).ToList();
        Assert.Empty(firstCodes.Intersect(secondCodes));
        Assert.Empty(secondCodes.Intersect(thirdCodes));
        Assert.Empty(firstCodes.Intersect(thirdCodes));
    }

    [Fact]
    public async Task GetAll_WithPageSizeAboveLimit_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems/routes?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems/routes?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeNegative_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems/routes?pageSize=-3");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems/routes?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageNegative_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems/routes?page=-1");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedTrue_ReturnsActiveAndDeleted()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_INC_SYS");
        var activeResp = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Ativa", "RT_INC_ACTIVE", sttId), TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var deletedCreate = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Deletada", "RT_INC_DELETED", sttId), TestApiClient.JsonOptions);
        var deletedDto = await deletedCreate.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/systems/routes/{deletedDto.Id}");

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&includeDeleted=true&pageSize=100");
        Assert.Contains(page.Data, r => r.Code == "RT_INC_ACTIVE" && r.DeletedAt == null);
        Assert.Contains(page.Data, r => r.Code == "RT_INC_DELETED" && r.DeletedAt != null);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedTrue_ReturnsRoutesOfSoftDeletedSystem()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_INC_DEL_SYS");
        var routeResp = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota Orfa", "RT_INC_ORPH", sttId), TestApiClient.JsonOptions);
        var routeDto = await routeResp.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(routeDto);

        // Soft-delete do sistema pai. A rota fica orfa: includeDeleted=false a esconde,
        // includeDeleted=true a expoe (cenario admin de auditoria).
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/{sysId}")).StatusCode);

        var hidden = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&pageSize=100");
        Assert.DoesNotContain(hidden.Data, r => r.Id == routeDto.Id);

        var visible = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&includeDeleted=true&pageSize=100");
        Assert.Contains(visible.Data, r => r.Id == routeDto.Id);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedDefault_HidesSoftDeleted()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_HID_SYS");
        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Para deletar", "RT_HID_DELETED", sttId), TestApiClient.JsonOptions);
        var dto = await resp.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&pageSize=100");
        Assert.DoesNotContain(page.Data, r => r.Code == "RT_HID_DELETED");
    }

    [Fact]
    public async Task GetAll_TotalReflectsFilters_BeforePagination()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_TOT_SYS");
        for (var i = 1; i <= 3; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/systems/routes",
                RouteCreateBody(sysId, $"Filtro {i}", $"FILTOTRT{i}", sttId), TestApiClient.JsonOptions);
        }
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Outro", "OUTROTOTRT", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&q=Filtro&page=1&pageSize=2");
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Data.Count);
    }

    [Fact]
    public async Task GetAll_PageBeyondTotal_ReturnsEmptyDataAnd200()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_BEYOND_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Unico", "RT_BEYOND_A", sttId), TestApiClient.JsonOptions);

        var resp = await _client.GetAsync($"/api/v1/systems/routes?systemId={sysId}&page=99&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PagedRoutesDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page.Data);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetAll_OrderingIsStable_NoDuplicatesAcrossPages()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_ORD_SYS");
        for (var i = 0; i < 7; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/systems/routes",
                RouteCreateBody(sysId, $"Ordem {i:D2}", $"RT_ORD_{i:D2}", sttId), TestApiClient.JsonOptions);
        }

        var first = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=1&pageSize=3");
        var second = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=2&pageSize=3");
        var third = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&page=3&pageSize=3");

        var collected = first.Data.Concat(second.Data).Concat(third.Data)
            .Select(r => r.Code)
            .ToList();
        Assert.Equal(collected.Count, collected.Distinct().Count());
        Assert.Equal(7, collected.Count);
    }

    [Fact]
    public async Task GetAll_OrderedByCode_Ascending()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysId = await CreateSystemAsync("RT_ORDA_SYS");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Z", "RT_ORDA_ZZZ", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "A", "RT_ORDA_AAA", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "M", "RT_ORDA_MMM", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysId}&pageSize=100");
        var codes = page.Data.Where(r => r.Code.StartsWith("RT_ORDA_", StringComparison.Ordinal))
            .Select(r => r.Code)
            .ToList();
        var sorted = codes.OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, codes);
    }

    [Fact]
    public async Task GetAll_CombinedFilters_ApplyTogether()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var sysA = await CreateSystemAsync("RT_COMB_A");
        var sysB = await CreateSystemAsync("RT_COMB_B");
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysA, "Listar usuarios", "RT_COMB_A_USERS", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysA, "Outra", "RT_COMB_A_OTHER", sttId), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysB, "Listar usuarios", "RT_COMB_B_USERS", sttId), TestApiClient.JsonOptions);

        var page = await GetRoutesPageAsync($"/api/v1/systems/routes?systemId={sysA}&q=USERS&pageSize=100");
        Assert.Single(page.Data);
        Assert.Equal("RT_COMB_A_USERS", page.Data[0].Code);
    }

    private async Task<PagedRoutesDto> GetRoutesPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedRoutesDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private sealed record SystemRefDto(Guid Id);
    private sealed record TokenTypeRefDto(Guid Id);
    private sealed record PermissionRefDto(Guid Id);
    private sealed record PermissionTypeRefDto(Guid Id);

    private sealed record DeleteConflictDto(string Message, int LinkedPermissionsCount);

    private sealed record RouteDto(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        Guid SystemTokenTypeId,
        string SystemTokenTypeCode,
        string SystemTokenTypeName,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record PagedRoutesDto(
        List<RouteDto> Data,
        int Page,
        int PageSize,
        int Total);
}
