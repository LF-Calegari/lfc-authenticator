using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Controllers.Auth;
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

        var listPage = await GetUsersPageAsync("/api/v1/users?pageSize=100");
        Assert.All(listPage.Data, u => Assert.Null(u.DeletedAt));
        Assert.Contains(listPage.Data, u => u.Email == "ativo@example.com");
        Assert.DoesNotContain(listPage.Data, u => u.Email == "outro@example.com");
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

        var listPage = await GetUsersPageAsync("/api/v1/users?pageSize=100");
        Assert.Contains(listPage.Data, u => u.Id == dto.Id);
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
        var page = await GetUsersDetailPageAsync("/api/v1/users?pageSize=100");
        var row = page.Data.First(u => u.Email == "list.empty@example.com");
        Assert.Empty(row.Roles);
        Assert.Empty(row.Permissions);
    }

    [Fact]
    public async Task GetById_UnknownUser_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForceLogout_HappyPath_OldTokenStartsToFailWith401_AndIncrementsTokenVersion()
    {
        // Cria target e faz login para obter um token "antigo".
        await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Force Target", "force.target@example.com"), TestApiClient.JsonOptions);

        var systemId = await TestApiClient.GetSystemIdAsync(_factory, TestApiClient.DefaultSystemCode);
        var anon = _factory.CreateApiClient();
        var login = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "force.target@example.com", password = "SenhaSegura1!", systemId },
            TestApiClient.JsonOptions);
        login.EnsureSuccessStatusCode();
        var loginDto = await login.Content.ReadFromJsonAsync<LoginDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);
        var oldToken = loginDto.Token;

        // Resolve o id do target para enviar à API.
        Guid targetId;
        int beforeTokenVersion;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "force.target@example.com");
            targetId = row.Id;
            beforeTokenVersion = row.TokenVersion;
        }

        var force = await _client.PostAsync($"/api/v1/users/{targetId}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.OK, force.StatusCode);

        using var payload = JsonDocument.Parse(await force.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        Assert.Equal("Sessões do usuário invalidadas com sucesso.", root.GetProperty("message").GetString());
        Assert.Equal(targetId, root.GetProperty("userId").GetGuid());
        Assert.Equal(beforeTokenVersion + 1, root.GetProperty("newTokenVersion").GetInt32());

        // Persistência: TokenVersion incrementado.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking().FirstAsync(u => u.Id == targetId);
            Assert.Equal(beforeTokenVersion + 1, row.TokenVersion);
        }

        // Token antigo deve falhar com 401 ao chamar endpoint protegido (claim tv não bate mais).
        using var verifyReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        verifyReq.Headers.Add(AuthController.SystemIdHeader, systemId.ToString("D"));
        verifyReq.Headers.Add(AuthController.RouteCodeHeader, "AUTH_V1_USERS_LIST");
        var verifyRes = await anon.SendAsync(verifyReq);
        Assert.Equal(HttpStatusCode.Unauthorized, verifyRes.StatusCode);
    }

    [Fact]
    public async Task ForceLogout_Idempotent_EachCallIncrementsTokenVersion()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Force Idem", "force.idem@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var first = await _client.PostAsync($"/api/v1/users/{dto.Id}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await _client.PostAsync($"/api/v1/users/{dto.Id}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.AsNoTracking().FirstAsync(u => u.Id == dto.Id);
        Assert.Equal(2, row.TokenVersion);
    }

    [Fact]
    public async Task ForceLogout_SelfTarget_ReturnsBadRequest()
    {
        // Resolve o id do caller atual (root) consultando o banco.
        Guid callerId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking().FirstAsync(u => u.Email == RootUserSeeder.RootEmail);
            callerId = row.Id;
        }

        var force = await _client.PostAsync($"/api/v1/users/{callerId}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, force.StatusCode);

        using var payload = JsonDocument.Parse(await force.Content.ReadAsStringAsync());
        var message = payload.RootElement.GetProperty("message").GetString();
        Assert.Contains("/auth/logout", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceLogout_TargetNotFound_Returns404()
    {
        var force = await _client.PostAsync($"/api/v1/users/{Guid.NewGuid()}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.NotFound, force.StatusCode);
    }

    [Fact]
    public async Task ForceLogout_TargetSoftDeleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Force Deleted", "force.deleted@example.com"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del = await _client.DeleteAsync($"/api/v1/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var force = await _client.PostAsync($"/api/v1/users/{dto.Id}/force-logout", content: null);
        Assert.Equal(HttpStatusCode.NotFound, force.StatusCode);
    }

    [Fact]
    public async Task ForceLogout_CallerWithoutPermission_Returns403()
    {
        // Cria usuário-target.
        var createTarget = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Target Privilege", "force.target.priv@example.com"), TestApiClient.JsonOptions);
        var target = await createTarget.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(target);

        // Cria caller sem nenhuma role/permissão (user comum) e faz login com ele.
        await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Caller No Perm", "caller.noperm@example.com"), TestApiClient.JsonOptions);

        var systemId = await TestApiClient.GetSystemIdAsync(_factory, TestApiClient.DefaultSystemCode);
        var anon = _factory.CreateApiClient();
        var login = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "caller.noperm@example.com", password = "SenhaSegura1!", systemId },
            TestApiClient.JsonOptions);
        login.EnsureSuccessStatusCode();
        var loginDto = await login.Content.ReadFromJsonAsync<LoginDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/users/{target.Id}/force-logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, systemId.ToString("D"));
        var response = await anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoFilters_UsesDefaultEnvelope()
    {
        var page = await GetUsersPageAsync("/api/v1/users");
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.Total >= page.Data.Count);
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsCorrectSubset()
    {
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/users",
                UserCreateBody($"Pag User {i:D2}", $"pag.user.{i:D2}@example.com"),
                TestApiClient.JsonOptions);
        }

        var first = await GetUsersPageAsync("/api/v1/users?q=Pag%20User&page=1&pageSize=2");
        var second = await GetUsersPageAsync("/api/v1/users?q=Pag%20User&page=2&pageSize=2");
        var third = await GetUsersPageAsync("/api/v1/users?q=Pag%20User&page=3&pageSize=2");

        Assert.Equal(2, first.Data.Count);
        Assert.Equal(2, second.Data.Count);
        Assert.Single(third.Data);
        Assert.Equal(5, first.Total);
        Assert.Equal(5, second.Total);
        Assert.Equal(5, third.Total);

        var ids = first.Data.Concat(second.Data).Concat(third.Data).Select(u => u.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnName()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Aparecida Silva Q", "aparecida.q@example.com"),
            TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetUsersPageAsync("/api/v1/users?q=Aparecida&pageSize=100");
        Assert.Contains(page.Data, u => u.Email == "aparecida.q@example.com");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnEmail()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Email Match", "email.match.unique@example.com"),
            TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var page = await GetUsersPageAsync("/api/v1/users?q=email.match.unique&pageSize=100");
        Assert.Contains(page.Data, u => u.Email == "email.match.unique@example.com");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_IsCaseInsensitive()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Roberto Oliveira Q", "roberto.q@example.com"),
            TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();

        var lower = await GetUsersPageAsync("/api/v1/users?q=roberto&pageSize=100");
        var upper = await GetUsersPageAsync("/api/v1/users?q=ROBERTO&pageSize=100");
        var mixed = await GetUsersPageAsync("/api/v1/users?q=RoBeRtO&pageSize=100");

        Assert.Contains(lower.Data, u => u.Email == "roberto.q@example.com");
        Assert.Contains(upper.Data, u => u.Email == "roberto.q@example.com");
        Assert.Contains(mixed.Data, u => u.Email == "roberto.q@example.com");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_EscapesUnderscoreLiteral()
    {
        // Sem o escape, "_" viraria wildcard ILIKE e casaria qualquer caractere unico no lugar.
        var resp1 = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Foo_Lit_BarUser", "foo.litbar1@example.com"),
            TestApiClient.JsonOptions);
        resp1.EnsureSuccessStatusCode();
        var resp2 = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("FooXLitXBarUser", "foo.litbar2@example.com"),
            TestApiClient.JsonOptions);
        resp2.EnsureSuccessStatusCode();

        var page = await GetUsersPageAsync("/api/v1/users?q=_Lit_&pageSize=100");
        Assert.Contains(page.Data, u => u.Name == "Foo_Lit_BarUser");
        Assert.DoesNotContain(page.Data, u => u.Name == "FooXLitXBarUser");
    }

    [Fact]
    public async Task GetAll_WithClientIdFilter_ReturnsOnlyMatching()
    {
        // Cria dois usuários, cada um vinculado ao seu próprio cliente auto-gerado pelo POST /users.
        var aResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ClientFilter A", "client.filter.a@example.com"),
            TestApiClient.JsonOptions);
        aResp.EnsureSuccessStatusCode();
        var aDto = await aResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(aDto);
        Assert.NotNull(aDto.ClientId);

        var bResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ClientFilter B", "client.filter.b@example.com"),
            TestApiClient.JsonOptions);
        bResp.EnsureSuccessStatusCode();
        var bDto = await bResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(bDto);
        Assert.NotNull(bDto.ClientId);

        var page = await GetUsersPageAsync($"/api/v1/users?clientId={aDto.ClientId}&pageSize=100");
        Assert.All(page.Data, u => Assert.Equal(aDto.ClientId, u.ClientId));
        Assert.Contains(page.Data, u => u.Id == aDto.Id);
        Assert.DoesNotContain(page.Data, u => u.Id == bDto.Id);
    }

    [Fact]
    public async Task GetAll_WithClientIdEmpty_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync($"/api/v1/users?clientId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithClientIdNonExistent_ReturnsEmpty()
    {
        var page = await GetUsersPageAsync($"/api/v1/users?clientId={Guid.NewGuid()}&pageSize=100");
        Assert.Empty(page.Data);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task GetAll_WithActiveTrue_ReturnsOnlyActive()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ActiveTrue Stay", "active.true.stay@example.com"),
            TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ActiveTrue Gone", "active.true.gone@example.com"),
            TestApiClient.JsonOptions);
        deletedResp.EnsureSuccessStatusCode();
        var deletedDto = await deletedResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/users/{deletedDto.Id}");

        var page = await GetUsersPageAsync("/api/v1/users?active=true&pageSize=100");
        Assert.Contains(page.Data, u => u.Id == activeDto.Id);
        Assert.DoesNotContain(page.Data, u => u.Id == deletedDto.Id);
    }

    [Fact]
    public async Task GetAll_WithActiveFalse_ReturnsOnlySoftDeleted()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ActiveFalse Stay", "active.false.stay@example.com"),
            TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("ActiveFalse Gone", "active.false.gone@example.com"),
            TestApiClient.JsonOptions);
        deletedResp.EnsureSuccessStatusCode();
        var deletedDto = await deletedResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/users/{deletedDto.Id}");

        var page = await GetUsersPageAsync("/api/v1/users?active=false&pageSize=100");
        Assert.Contains(page.Data, u => u.Id == deletedDto.Id);
        Assert.DoesNotContain(page.Data, u => u.Id == activeDto.Id);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedTrue_ReturnsActiveAndSoftDeleted()
    {
        var activeResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Incl Active", "incl.active.user@example.com"),
            TestApiClient.JsonOptions);
        activeResp.EnsureSuccessStatusCode();
        var activeDto = await activeResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(activeDto);

        var deletedResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Incl Deleted", "incl.deleted.user@example.com"),
            TestApiClient.JsonOptions);
        deletedResp.EnsureSuccessStatusCode();
        var deletedDto = await deletedResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deletedDto);
        await _client.DeleteAsync($"/api/v1/users/{deletedDto.Id}");

        var page = await GetUsersPageAsync("/api/v1/users?includeDeleted=true&pageSize=100");
        Assert.Contains(page.Data, u => u.Id == activeDto.Id && u.DeletedAt == null);
        Assert.Contains(page.Data, u => u.Id == deletedDto.Id && u.DeletedAt != null);
    }

    [Fact]
    public async Task GetAll_WithActiveAndIncludeDeleted_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/users?active=true&includeDeleted=true");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeAboveLimit_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/users?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/users?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/users?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageBeyondTotal_ReturnsEmptyDataAnd200()
    {
        await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Beyond Unico", "beyond.unico@example.com"),
            TestApiClient.JsonOptions);

        var resp = await _client.GetAsync("/api/v1/users?q=beyond.unico&page=99&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PagedUsersDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page.Data);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetAll_OrderedByCreatedAt_Descending()
    {
        var first = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Order User AAA", "order.user.aaa@example.com"),
            TestApiClient.JsonOptions);
        first.EnsureSuccessStatusCode();
        var firstDto = await first.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(firstDto);

        await Task.Delay(20);

        var second = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Order User BBB", "order.user.bbb@example.com"),
            TestApiClient.JsonOptions);
        second.EnsureSuccessStatusCode();
        var secondDto = await second.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(secondDto);

        var page = await GetUsersPageAsync("/api/v1/users?q=Order%20User&pageSize=100");
        var ordered = page.Data
            .Where(u => u.Name.StartsWith("Order User", StringComparison.Ordinal))
            .Select(u => u.Id)
            .ToList();
        Assert.Equal(secondDto.Id, ordered[0]);
        Assert.Equal(firstDto.Id, ordered[1]);
    }

    [Fact]
    public async Task GetAll_TotalReflectsFilters_BeforePagination()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/users",
                UserCreateBody($"FiltroTot {i}", $"filtrotot.{i}@example.com"),
                TestApiClient.JsonOptions);
        }
        await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Outro Nome U", "outro.nome.u@example.com"),
            TestApiClient.JsonOptions);

        var page = await GetUsersPageAsync("/api/v1/users?q=FiltroTot&page=1&pageSize=2");
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Data.Count);
    }

    [Fact]
    public async Task GetAll_CombinedFilters_ApplyTogether()
    {
        var aResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Combo Maria User", "combo.maria.user@example.com"),
            TestApiClient.JsonOptions);
        aResp.EnsureSuccessStatusCode();
        var aDto = await aResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(aDto);
        Assert.NotNull(aDto.ClientId);

        await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("Combo Maria Outro", "combo.maria.outro@example.com"),
            TestApiClient.JsonOptions);

        var page = await GetUsersPageAsync(
            $"/api/v1/users?q=Combo%20Maria&clientId={aDto.ClientId}&active=true&pageSize=100");
        Assert.All(page.Data, u => Assert.Equal(aDto.ClientId, u.ClientId));
        Assert.Contains(page.Data, u => u.Id == aDto.Id);
        Assert.DoesNotContain(page.Data, u => u.Email == "combo.maria.outro@example.com");
    }

    [Fact]
    public async Task GetAll_WithIdsPresent_PreservesMinimalProjectionContract()
    {
        // Mesmo passando page/pageSize e demais filtros, o caminho `?ids=` deve permanecer
        // retornando o array minimal na ordem solicitada (sem envelope paginado).
        var aResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("IdsKeep One", "ids.keep.one@example.com"),
            TestApiClient.JsonOptions);
        aResp.EnsureSuccessStatusCode();
        var aDto = await aResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(aDto);

        var bResp = await _client.PostAsJsonAsync("/api/v1/users",
            UserCreateBody("IdsKeep Two", "ids.keep.two@example.com"),
            TestApiClient.JsonOptions);
        bResp.EnsureSuccessStatusCode();
        var bDto = await bResp.Content.ReadFromJsonAsync<UserDto>(TestApiClient.JsonOptions);
        Assert.NotNull(bDto);

        var resp = await _client.GetAsync(
            $"/api/v1/users?ids={bDto.Id},{aDto.Id}&page=1&pageSize=20&q=ignored");
        resp.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, payload.RootElement.ValueKind);
        var rows = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(bDto.Id, rows[0].GetProperty("id").GetGuid());
        Assert.Equal(aDto.Id, rows[1].GetProperty("id").GetGuid());
        Assert.Equal(3, rows[0].EnumerateObject().Count());
    }

    private async Task<PagedUsersDto> GetUsersPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedUsersDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private async Task<PagedUsersDetailDto> GetUsersDetailPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedUsersDetailDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private sealed record PagedUsersDto(
        List<UserDto> Data,
        int Page,
        int PageSize,
        int Total);

    private sealed record PagedUsersDetailDto(
        List<UserDetailDto> Data,
        int Page,
        int PageSize,
        int Total);

    private sealed record LoginDto(string Token);

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
