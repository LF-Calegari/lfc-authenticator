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

    private static object UserUpdateBody(string name, string email, string password = "SenhaSegura1!",
        int identity = 1, bool active = true) =>
        new { name, email, password, identity, active };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithUtcTimestamps_AndNullDeletedAt()
    {
        var response = await _client.PostAsJsonAsync("/users",
            UserCreateBody("Usuário X", "usuario.x@example.com"), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
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
        await _client.PostAsJsonAsync("/users", UserCreateBody("A", "dup@example.com"), JsonOptions);

        var response = await _client.PostAsJsonAsync("/users", UserCreateBody("B", "dup@example.com"), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateEmail_NormalizesWhitespaceAndCase_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/users", UserCreateBody("A", "same@example.com"), JsonOptions);

        var response = await _client.PostAsJsonAsync("/users",
            UserCreateBody("B", "  SAME@EXAMPLE.COM  "), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateEmail_NormalizesCase_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/users", UserCreateBody("A", "a_mail@example.com"), JsonOptions);
        var b = await _client.PostAsJsonAsync("/users", UserCreateBody("B", "b_mail@example.com"), JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/users/{dtoB.Id}",
            UserUpdateBody("B2", "A_MAIL@EXAMPLE.COM"), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameEmail_OneCreatedOneConflict()
    {
        const string email = "concurrent@example.com";
        var bodyA = UserCreateBody("A", email);
        var bodyB = UserCreateBody("B", email);
        var t1 = _client.PostAsJsonAsync("/users", bodyA, JsonOptions);
        var t2 = _client.PostAsJsonAsync("/users", bodyB, JsonOptions);
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
        var response = await _client.PostAsJsonAsync("/users",
            UserCreateBody("   ", "n1@example.com"), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/users",
            UserCreateBody("Nome", "\t  "), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/users",
            new { name = "N", email = "p1@example.com", password = "   ", identity = 1, active = true }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/users", UserCreateBody("S", "w1@example.com"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/users/{dto.Id}",
            UserUpdateBody("   ", "w1@example.com"), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveUsers()
    {
        await _client.PostAsJsonAsync("/users", UserCreateBody("Ativo", "ativo@example.com"), JsonOptions);

        var other = await _client.PostAsJsonAsync("/users", UserCreateBody("Outro", "outro@example.com"), JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/users/{toDelete.Id}");

        var listResp = await _client.GetAsync("/users");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, u => Assert.Null(u.DeletedAt));
        Assert.Contains(list, u => u.Email == "ativo@example.com");
        Assert.DoesNotContain(list, u => u.Email == "outro@example.com");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/users", UserCreateBody("S", "g1@example.com"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/users/{dto.Id}");
        var get404 = await _client.GetAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/users", UserCreateBody("S", "u1@example.com"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/users/{dto.Id}",
            UserUpdateBody("S2", "u1@example.com", identity: 2, active: false), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/users/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/users/{dto.Id}",
            UserUpdateBody("S3", "u1@example.com"), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/users", UserCreateBody("S", "d1@example.com"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/users", UserCreateBody("S", "r1@example.com"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/users/{dto.Id}");

        var get404 = await _client.GetAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/users/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/users/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);

        var listResp = await _client.GetAsync("/users");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<UserDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list, u => u.Id == dto.Id);
    }

    private sealed record UserDto(
        Guid Id,
        string Name,
        string Email,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
