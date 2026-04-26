using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

/// <summary>
/// Paridade de cenários com <see cref="SystemsApiTests"/> (soft delete, 404, conflito de unicidade, etc.).
/// </summary>
public class UsersApiTests : IAsyncLifetime
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

    private static object UserUpdateBody(string name, string email, int identity = 1, bool active = true) =>
        new { name, email, identity, active };

    private static object UserUpdateBodyWithClientId(string name, string email, Guid clientId, int identity = 1,
        bool active = true) =>
        new { name, email, clientId, identity, active };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithUtcTimestamps_AndNullDeletedAt()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Usuário X", "usuario.x@example.com"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Usuário X", dto.Name);
        Assert.Equal("usuario.x@example.com", dto.Email);
        Assert.True(dto.Active);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateEmail_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("A", "dup@example.com"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("B", "dup@example.com"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateEmail_NormalizesWhitespaceAndCase_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("A", "same@example.com"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("B", "  SAME@EXAMPLE.COM  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateEmail_NormalizesCase_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("A", "a_mail@example.com"), TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("B", "b_mail@example.com"), TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/api/v1/users/{dtoB.Id}",
            UserUpdateBody("B2", "A_MAIL@EXAMPLE.COM"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameEmail_OneCreatedOneConflict()
    {
        const string email = "concurrent@example.com";
        var bodyA = UserCreateBody("A", email);
        var bodyB = UserCreateBody("B", email);
        var t1 = _client.PostAsJsonAsync("/api/v1/users", bodyA, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/api/v1/users", bodyB, TestApiClient.JsonOptions);
        await Task.WhenAll(t1, t2);

        var r1 = await t1;
        var r2 = await t2;
        var statuses = new[] { r1.StatusCode, r2.StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("   ", "n1@example.com"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Nome", "\t  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new { name = "N", email = "p1@example.com", password = "   ", identity = 1, active = true }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutIdentity_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new { name = "Sem Identity", email = "no.identity@example.com", password = "SenhaSegura1!", active = true },
            TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "w1@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}",
            UserUpdateBody("   ", "w1@example.com"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveUsers()
    {
        await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("Ativo", "ativo@example.com"), TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("Outro", "outro@example.com"), TestApiClient.JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/api/v1/users/{toDelete.Id}");

        var listResp = await _client.GetAsync("/api/v1/users");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, u => Assert.Null(u.DeletedAt));
        Assert.Contains(list, u => u.Email == "ativo@example.com");
        Assert.DoesNotContain(list, u => u.Email == "outro@example.com");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "g1@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        using (var payload = JsonDocument.Parse(await getOk.Content.ReadAsStringAsync()))
        {
            var root = payload.RootElement;
            Assert.Equal(3, root.EnumerateObject().Count());
            Assert.Equal(dto.Id, root.GetProperty("id").GetGuid());
            Assert.Equal("S", root.GetProperty("name").GetString());
            Assert.Equal("g1@example.com", root.GetProperty("email").GetString());
        }

        await _client.DeleteAsync($"/api/v1/users/{dto.Id}");
        var get404 = await _client.GetAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "u1@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}",
            UserUpdateBody("S2", "u1@example.com", identity: 2, active: false), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/users/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}",
            UserUpdateBody("S3", "u1@example.com"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutIdentityOrActive_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "missing.update@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}",
            new { name = "S2", email = "missing.update@example.com" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutClientId_KeepsExistingAssociation()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Com Cliente", "keep.client@example.com"), TestApiClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotNull(dto.ClientId);

        var put = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}",
            UserUpdateBody("Com Cliente Atualizado", "keep.client@example.com", identity: 2, active: true),
            TestApiClient.JsonOptions);
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(dto.ClientId, updated.ClientId);
    }

    [Fact]
    public async Task Update_WithExplicitClientId_ReassociatesUser()
    {
        var createUser = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Associado", "explicit.client@example.com"), TestApiClient.JsonOptions);
        createUser.EnsureSuccessStatusCode();
        var user = await createUser.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(user);
        Assert.NotNull(user.ClientId);

        var createClient = await _client.PostAsJsonAsync("/api/v1/clients", new
        {
            type = "PF",
            cpf = GenerateCpf(200000000),
            fullName = "Cliente Destino"
        }, TestApiClient.JsonOptions);
        createClient.EnsureSuccessStatusCode();
        var client = await createClient.Content.ReadFromJsonAsync<ClientIdDto>(TestApiClient.JsonOptions);
        Assert.NotNull(client);

        var put = await _client.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            UserUpdateBodyWithClientId("Associado", "explicit.client@example.com", client.Id),
            TestApiClient.JsonOptions);
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(client.Id, updated.ClientId);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "d1@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task UpdatePassword_Put_ThenLoginWithNewPassword_ReturnsOk()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("P", "pwd.change@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putPwd = await _client.PutAsJsonAsync($"/api/v1/users/{dto.Id}/password",
            new { password = "NovaSenhaSegura2!" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putPwd.StatusCode);

        var systemId = await TestApiClient.GetSystemIdAsync(_factory, TestApiClient.DefaultSystemCode);
        var anon = _factory.CreateApiClient();
        var oldLogin = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "pwd.change@example.com", password = "SenhaSegura1!", systemId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "pwd.change@example.com", password = "NovaSenhaSegura2!", systemId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("S", "r1@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/users/{dto.Id}");

        var get404 = await _client.GetAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var restore = await _client.PostAsync($"/api/v1/users/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);

        var listResp = await _client.GetAsync("/api/v1/users");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list, u => u.Id == dto.Id);
    }

    [Fact]
    public async Task GetByIds_ReturnsMinimalProjection_InRequestedOrder()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("Com vínculo", "batch.one@example.com"),
            TestApiClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var userA = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(userA);

        var createSecond = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Dois", "batch.two@example.com"), TestApiClient.JsonOptions);
        createSecond.EnsureSuccessStatusCode();
        var userB = await createSecond.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(userB);

        var getResp = await _client.GetAsync($"/api/v1/users?ids={userB.Id},{userA.Id}");
        getResp.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        var rows = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        Assert.Equal(userB.Id, rows[0].GetProperty("id").GetGuid());
        Assert.Equal("Dois", rows[0].GetProperty("name").GetString());
        Assert.Equal("batch.two@example.com", rows[0].GetProperty("email").GetString());
        Assert.Equal(3, rows[0].EnumerateObject().Count());

        Assert.Equal(userA.Id, rows[1].GetProperty("id").GetGuid());
        Assert.Equal("Com vínculo", rows[1].GetProperty("name").GetString());
        Assert.Equal("batch.one@example.com", rows[1].GetProperty("email").GetString());
        Assert.Equal(3, rows[1].EnumerateObject().Count());
    }

    [Fact]
    public async Task GetByIds_InvalidGuid_ReturnsBadRequest()
    {
        var getResp = await _client.GetAsync("/api/v1/users?ids=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, getResp.StatusCode);
    }

    [Fact]
    public async Task GetByIds_EmptyValue_ReturnsBadRequest()
    {
        var getResp = await _client.GetAsync("/api/v1/users?ids=");
        Assert.Equal(HttpStatusCode.BadRequest, getResp.StatusCode);
    }

    [Fact]
    public async Task GetByIds_MoreThanMaxBatchIds_ReturnsBadRequest()
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid().ToString());
        var query = string.Join(',', ids);

        var getResp = await _client.GetAsync($"/api/v1/users?ids={query}");
        Assert.Equal(HttpStatusCode.BadRequest, getResp.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyRolesAndPermissions_OnEachUser()
    {
        await _client.PostAsJsonAsync("/api/v1/users", UserCreateBody("Lista", "list.empty@example.com"), TestApiClient.JsonOptions);
        var listResp = await _client.GetAsync("/api/v1/users");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserDetailDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        var row = list.First(u => u.Email == "list.empty@example.com");
        Assert.Empty(row.Roles);
        Assert.Empty(row.Permissions);
    }

    [Fact]
    public async Task GetById_UnknownUser_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record UserDto(
        Guid Id,
        string Name,
        string Email,
        Guid? ClientId,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record UserDetailDto(
        Guid Id,
        string Name,
        string Email,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt,
        List<UserRoleLinkDto> Roles,
        List<UserPermissionLinkDto> Permissions);

    private sealed record UserRoleLinkDto(
        int Id,
        Guid UserId,
        Guid RoleId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record UserPermissionLinkDto(
        Guid Id,
        Guid UserId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record ClientIdDto(Guid Id);

    private static string GenerateCpf(int baseDigits)
    {
        var nine = (baseDigits % 1_000_000_000).ToString("D9");
        var d1 = CheckDigit(nine, 10);
        var d2 = CheckDigit(nine + d1, 11);
        return nine + d1 + d2;
    }

    private static int CheckDigit(string input, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
            sum += (input[i] - '0') * (startWeight - i);
        var result = (sum * 10) % 11;
        return result == 10 ? 0 : result;
    }
}
