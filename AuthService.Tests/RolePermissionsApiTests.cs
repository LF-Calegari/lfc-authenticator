using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RolePermissionsApiTests : IAsyncLifetime
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

    private async Task<Guid> CreateRoleAsync(string code)
    {
        var r = await _client.PostAsJsonAsync("/v1/roles",
            new { name = "R", code, }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateSystemAsync(string code)
    {
        var r = await _client.PostAsJsonAsync("/v1/systems",
            new { name = "S", code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionTypeAsync(string code)
    {
        var r = await _client.PostAsJsonAsync("/v1/permissions/types",
            new { name = "T", code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionAsync(Guid systemId, Guid permissionTypeId)
    {
        var r = await _client.PostAsJsonAsync("/v1/permissions",
            new { systemId, permissionTypeId, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object RolePermissionBody(Guid roleId, Guid permissionId) => new { roleId, permissionId };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithGuidId_UtcAndNullDeletedAt()
    {
        var roleId = await CreateRoleAsync("RP_CREATE");
        var sysId = await CreateSystemAsync("SYS_RP_CREATE");
        var typeId = await CreatePermissionTypeAsync("PT_RP_CREATE");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        var response = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(roleId, dto.RoleId);
        Assert.Equal(permissionId, dto.PermissionId);
        Assert.Null(dto.DeletedAt);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateRoleIdPermissionId_ReturnsConflict()
    {
        var roleId = await CreateRoleAsync("RP_DUP");
        var sysId = await CreateSystemAsync("SYS_RP_DUP");
        var typeId = await CreatePermissionTypeAsync("PT_RP_DUP");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidRoleId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("SYS_RP_INV_R");
        var typeId = await CreatePermissionTypeAsync("PT_RP_INV_R");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        var response = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(Guid.NewGuid(), permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPermissionId_ReturnsBadRequest()
    {
        var roleId = await CreateRoleAsync("RP_INV_P");

        var response = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSamePair_OneCreatedOneConflict()
    {
        var roleId = await CreateRoleAsync("RP_CONC");
        var sysId = await CreateSystemAsync("SYS_RP_CONC");
        var typeId = await CreatePermissionTypeAsync("PT_RP_CONC");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var body = RolePermissionBody(roleId, permissionId);
        var t1 = _client.PostAsJsonAsync("/v1/roles-permissions", body, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/v1/roles-permissions", body, TestApiClient.JsonOptions);
        await Task.WhenAll(t1, t2);

        var r1 = await t1;
        var r2 = await t2;
        var statuses = new[] { r1.StatusCode, r2.StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveLinks_WithActiveParents()
    {
        var roleId = await CreateRoleAsync("RP_LIST");
        var sysId = await CreateSystemAsync("SYS_RP_LIST");
        var typeId = await CreatePermissionTypeAsync("PT_RP_LIST");
        var permA = await CreatePermissionAsync(sysId, typeId);
        var permB = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permA), TestApiClient.JsonOptions);
        var second = await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permB), TestApiClient.JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(secondDto);
        await _client.DeleteAsync($"/v1/roles-permissions/{secondDto.Id}");

        var listResp = await _client.GetAsync("/v1/roles-permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RolePermissionDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, x => Assert.Null(x.DeletedAt));
        Assert.Contains(list, x => x.PermissionId == permA);
        Assert.DoesNotContain(list, x => x.PermissionId == permB);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var roleId = await CreateRoleAsync("RP_GET");
        var sysId = await CreateSystemAsync("SYS_RP_GET");
        var typeId = await CreatePermissionTypeAsync("PT_RP_GET");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/v1/roles-permissions/{dto.Id}");
        var get404 = await _client.GetAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Restore_ActiveLink_ReturnsNotFound()
    {
        var roleId = await CreateRoleAsync("RP_RS_ACT");
        var sysId = await CreateSystemAsync("SYS_RP_RS_ACT");
        var typeId = await CreatePermissionTypeAsync("PT_RP_RS_ACT");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/roles-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetWorksAgain()
    {
        var roleId = await CreateRoleAsync("RP_RS_OK");
        var sysId = await CreateSystemAsync("SYS_RP_RS_OK");
        var typeId = await CreatePermissionTypeAsync("PT_RP_RS_OK");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/roles-permissions/{dto.Id}");

        var get404 = await _client.GetAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/roles-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Restore_WhenRoleDeleted_ReturnsBadRequest()
    {
        var roleId = await CreateRoleAsync("RP_RS_BAD_R");
        var sysId = await CreateSystemAsync("SYS_RP_RS_BAD_R");
        var typeId = await CreatePermissionTypeAsync("PT_RP_RS_BAD_R");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/roles-permissions/{dto.Id}");
        await _client.DeleteAsync($"/v1/roles/{roleId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/roles-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicatePair_ReturnsConflict()
    {
        var roleId = await CreateRoleAsync("RP_PUT_DUP");
        var sysId = await CreateSystemAsync("SYS_RP_PUT");
        var typeId = await CreatePermissionTypeAsync("PT_RP_PUT");
        var permA = await CreatePermissionAsync(sysId, typeId);
        var permB = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permA), TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permB), TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/v1/roles-permissions/{dtoB.Id}",
            RolePermissionBody(roleId, permA), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var roleId = await CreateRoleAsync("RP_DEL");
        var sysId = await CreateSystemAsync("SYS_RP_DEL");
        var typeId = await CreatePermissionTypeAsync("PT_RP_DEL");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/roles-permissions",
            RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RolePermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.RolePermissions.IgnoreQueryFilters().SingleAsync(rp => rp.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/v1/roles-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task GetAll_HidesLinkWhenPermissionDeleted()
    {
        var roleId = await CreateRoleAsync("RP_HIDE_P");
        var sysId = await CreateSystemAsync("SYS_RP_HIDE");
        var typeId = await CreatePermissionTypeAsync("PT_RP_HIDE");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);

        await _client.DeleteAsync($"/v1/permissions/{permissionId}");

        var listResp = await _client.GetAsync("/v1/roles-permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RolePermissionDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, x => x.PermissionId == permissionId);
    }

    [Fact]
    public async Task GetAll_HidesLinkWhenRoleDeleted()
    {
        var roleId = await CreateRoleAsync("RP_HIDE_R");
        var sysId = await CreateSystemAsync("SYS_RP_HIDE_R");
        var typeId = await CreatePermissionTypeAsync("PT_RP_HIDE_R");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        await _client.PostAsJsonAsync("/v1/roles-permissions", RolePermissionBody(roleId, permissionId), TestApiClient.JsonOptions);

        await _client.DeleteAsync($"/v1/roles/{roleId}");

        var listResp = await _client.GetAsync("/v1/roles-permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RolePermissionDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, x => x.RoleId == roleId);
    }

    private sealed record RefGuidDto(Guid Id);

    private sealed record RolePermissionDto(
        Guid Id,
        Guid RoleId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
