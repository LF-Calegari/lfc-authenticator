using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class UserPermissionsApiTests : IAsyncLifetime
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

    private static object UserCreateBody(string name, string email, string password = "SenhaSegura1!",
        int identity = 1, bool active = true) =>
        new { name, email, password, identity, active };

    private async Task<Guid> CreateUserAsync(string emailLocal, bool active = true)
    {
        var email = $"{emailLocal}@up.test";
        var r = await _client.PostAsJsonAsync("/v1/users", UserCreateBody("U", email, active: active), TestApiClient.JsonOptions);
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

    private static object UserPermissionBody(Guid userId, Guid permissionId) => new { userId, permissionId };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithGuidId_UtcAndNullDeletedAt()
    {
        var userId = await CreateUserAsync("up_create");
        var sysId = await CreateSystemAsync("SYS_UP_CREATE");
        var typeId = await CreatePermissionTypeAsync("PT_UP_CREATE");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        var response = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal(permissionId, dto.PermissionId);
        Assert.Null(dto.DeletedAt);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateUserIdPermissionId_ReturnsConflict()
    {
        var userId = await CreateUserAsync("up_dup");
        var sysId = await CreateSystemAsync("SYS_UP_DUP");
        var typeId = await CreatePermissionTypeAsync("PT_UP_DUP");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidUserId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("SYS_UP_INV_U");
        var typeId = await CreatePermissionTypeAsync("PT_UP_INV_U");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        var response = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(Guid.NewGuid(), permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPermissionId_ReturnsBadRequest()
    {
        var userId = await CreateUserAsync("up_inv_p");

        var response = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InactiveUser_ReturnsBadRequest_WithInactiveMessage()
    {
        var userId = await CreateUserAsync("up_inactive", active: false);
        var sysId = await CreateSystemAsync("SYS_UP_INACT");
        var typeId = await CreatePermissionTypeAsync("PT_UP_INACT");
        var permissionId = await CreatePermissionAsync(sysId, typeId);

        var response = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Usuário inativo", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ConcurrentSamePair_OneCreatedOneConflict()
    {
        var userId = await CreateUserAsync("up_conc");
        var sysId = await CreateSystemAsync("SYS_UP_CONC");
        var typeId = await CreatePermissionTypeAsync("PT_UP_CONC");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var body = UserPermissionBody(userId, permissionId);
        var t1 = _client.PostAsJsonAsync("/v1/users-permissions", body, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/v1/users-permissions", body, TestApiClient.JsonOptions);
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
        var userId = await CreateUserAsync("up_list");
        var sysId = await CreateSystemAsync("SYS_UP_LIST");
        var typeId = await CreatePermissionTypeAsync("PT_UP_LIST");
        var permA = await CreatePermissionAsync(sysId, typeId);
        var permB = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permA), TestApiClient.JsonOptions);
        var second = await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permB), TestApiClient.JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(secondDto);
        await _client.DeleteAsync($"/v1/users-permissions/{secondDto.Id}");

        var listResp = await _client.GetAsync("/v1/users-permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserPermissionDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, x => Assert.Null(x.DeletedAt));
        Assert.Contains(list, x => x.PermissionId == permA);
        Assert.DoesNotContain(list, x => x.PermissionId == permB);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var userId = await CreateUserAsync("up_get");
        var sysId = await CreateSystemAsync("SYS_UP_GET");
        var typeId = await CreatePermissionTypeAsync("PT_UP_GET");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/v1/users-permissions/{dto.Id}");
        var get404 = await _client.GetAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Restore_ActiveLink_ReturnsNotFound()
    {
        var userId = await CreateUserAsync("up_rs_act");
        var sysId = await CreateSystemAsync("SYS_UP_RS_ACT");
        var typeId = await CreatePermissionTypeAsync("PT_UP_RS_ACT");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/users-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetWorksAgain()
    {
        var userId = await CreateUserAsync("up_rs_ok");
        var sysId = await CreateSystemAsync("SYS_UP_RS_OK");
        var typeId = await CreatePermissionTypeAsync("PT_UP_RS_OK");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/users-permissions/{dto.Id}");

        var get404 = await _client.GetAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/users-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Restore_WhenUserDeleted_ReturnsBadRequest()
    {
        var userId = await CreateUserAsync("up_rs_bad_u");
        var sysId = await CreateSystemAsync("SYS_UP_RS_BAD_U");
        var typeId = await CreatePermissionTypeAsync("PT_UP_RS_BAD_U");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/users-permissions/{dto.Id}");
        await _client.DeleteAsync($"/v1/users/{userId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/users-permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicatePair_ReturnsConflict()
    {
        var userId = await CreateUserAsync("up_put_dup");
        var sysId = await CreateSystemAsync("SYS_UP_PUT");
        var typeId = await CreatePermissionTypeAsync("PT_UP_PUT");
        var permA = await CreatePermissionAsync(sysId, typeId);
        var permB = await CreatePermissionAsync(sysId, typeId);

        await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permA), TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permB), TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/v1/users-permissions/{dtoB.Id}",
            UserPermissionBody(userId, permA), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var userId = await CreateUserAsync("up_del");
        var sysId = await CreateSystemAsync("SYS_UP_DEL");
        var typeId = await CreatePermissionTypeAsync("PT_UP_DEL");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        var create = await _client.PostAsJsonAsync("/v1/users-permissions",
            UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserPermissionDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserPermissions.IgnoreQueryFilters().SingleAsync(up => up.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/v1/users-permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task GetAll_HidesLinkWhenPermissionDeleted()
    {
        var userId = await CreateUserAsync("up_hide_p");
        var sysId = await CreateSystemAsync("SYS_UP_HIDE");
        var typeId = await CreatePermissionTypeAsync("PT_UP_HIDE");
        var permissionId = await CreatePermissionAsync(sysId, typeId);
        await _client.PostAsJsonAsync("/v1/users-permissions", UserPermissionBody(userId, permissionId), TestApiClient.JsonOptions);

        await _client.DeleteAsync($"/v1/permissions/{permissionId}");

        var listResp = await _client.GetAsync("/v1/users-permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserPermissionDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, x => x.PermissionId == permissionId);
    }

    private sealed record RefGuidDto(Guid Id);

    private sealed record UserPermissionDto(
        Guid Id,
        Guid UserId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
