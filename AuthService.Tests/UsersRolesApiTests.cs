using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class UsersRolesApiTests : IAsyncLifetime
{
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
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

    private async Task<Guid> CreateUserAsync(string emailLocal)
    {
        var email = $"{emailLocal}@ur.test";
        var r = await _client.PostAsJsonAsync("/users", UserCreateBody("U", email), JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateRoleAsync(string code)
    {
        var r = await _client.PostAsJsonAsync("/roles",
            new { name = "R", code }, JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefGuidDto>(JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object UserRoleBody(Guid userId, Guid roleId) => new { userId, roleId };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithIdentityId_UtcAndNullDeletedAt()
    {
        var userId = await CreateUserAsync("ur_create");
        var roleId = await CreateRoleAsync("ROLE_UR_CREATE");

        var response = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.True(dto.Id > 0);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal(roleId, dto.RoleId);
        Assert.Null(dto.DeletedAt);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateUserIdRoleId_ReturnsConflict()
    {
        var userId = await CreateUserAsync("ur_dup");
        var roleId = await CreateRoleAsync("ROLE_UR_DUP");

        await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);

        var response = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidUserId_ReturnsBadRequest()
    {
        var roleId = await CreateRoleAsync("ROLE_UR_INV_U");

        var response = await _client.PostAsJsonAsync("/users-roles",
            UserRoleBody(Guid.NewGuid(), roleId), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidRoleId_ReturnsBadRequest()
    {
        var userId = await CreateUserAsync("ur_inv_r");

        var response = await _client.PostAsJsonAsync("/users-roles",
            UserRoleBody(userId, Guid.NewGuid()), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSamePair_OneCreatedOneConflict()
    {
        var userId = await CreateUserAsync("ur_conc");
        var roleId = await CreateRoleAsync("ROLE_UR_CONC");
        var body = UserRoleBody(userId, roleId);
        var t1 = _client.PostAsJsonAsync("/users-roles", body, JsonOptions);
        var t2 = _client.PostAsJsonAsync("/users-roles", body, JsonOptions);
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
        var userId = await CreateUserAsync("ur_list");
        var roleA = await CreateRoleAsync("ROLE_UR_LIST_A");
        var roleB = await CreateRoleAsync("ROLE_UR_LIST_B");

        await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleA), JsonOptions);
        var second = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleB), JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(secondDto);
        await _client.DeleteAsync($"/users-roles/{secondDto.Id}");

        var listResp = await _client.GetAsync("/users-roles");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserRoleDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, x => Assert.Null(x.DeletedAt));
        Assert.Contains(list, x => x.RoleId == roleA);
        Assert.DoesNotContain(list, x => x.RoleId == roleB);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var userId = await CreateUserAsync("ur_get");
        var roleId = await CreateRoleAsync("ROLE_UR_GET");
        var create = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/users-roles/{dto.Id}");
        var get404 = await _client.GetAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Restore_ActiveLink_ReturnsNotFound()
    {
        var userId = await CreateUserAsync("ur_rs_act");
        var roleId = await CreateRoleAsync("ROLE_UR_RS_ACT");
        var create = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/users-roles/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetWorksAgain()
    {
        var userId = await CreateUserAsync("ur_rs_ok");
        var roleId = await CreateRoleAsync("ROLE_UR_RS_OK");
        var create = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/users-roles/{dto.Id}");

        var get404 = await _client.GetAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/users-roles/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Restore_WhenUserDeleted_ReturnsBadRequest()
    {
        var userId = await CreateUserAsync("ur_rs_bad_u");
        var roleId = await CreateRoleAsync("ROLE_UR_RS_BAD_U");
        var create = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/users-roles/{dto.Id}");
        await _client.DeleteAsync($"/users/{userId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/users-roles/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicatePair_ReturnsConflict()
    {
        var userId = await CreateUserAsync("ur_put_dup");
        var roleA = await CreateRoleAsync("ROLE_UR_PUT_A");
        var roleB = await CreateRoleAsync("ROLE_UR_PUT_B");

        await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleA), JsonOptions);
        var b = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleB), JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/users-roles/{dtoB.Id}", UserRoleBody(userId, roleA), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var userId = await CreateUserAsync("ur_del");
        var roleId = await CreateRoleAsync("ROLE_UR_DEL");
        var create = await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserRoles.IgnoreQueryFilters().SingleAsync(ur => ur.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/users-roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task GetAll_HidesLinkWhenRoleDeleted()
    {
        var userId = await CreateUserAsync("ur_hide_r");
        var roleId = await CreateRoleAsync("ROLE_UR_HIDE_R");
        await _client.PostAsJsonAsync("/users-roles", UserRoleBody(userId, roleId), JsonOptions);

        await _client.DeleteAsync($"/roles/{roleId}");

        var listResp = await _client.GetAsync("/users-roles");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserRoleDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, x => x.RoleId == roleId);
    }

    private sealed record RefGuidDto(Guid Id);

    private sealed record UserRoleDto(
        int Id,
        Guid UserId,
        Guid RoleId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
