using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RolesApiTests : IAsyncLifetime
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

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithUtcTimestamps_AndNullDeletedAt()
    {
        var body = new { name = "Administrador", code = "ROLE_ADMIN" };
        var response = await _client.PostAsJsonAsync("/roles", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Administrador", dto.Name);
        Assert.Equal("ROLE_ADMIN", dto.Code);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/roles", new { name = "A", code = "ROLE_ABC" }, JsonOptions);

        var response = await _client.PostAsJsonAsync("/roles", new { name = "B", code = "ROLE_ABC" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/roles", new { name = "A", code = "ROLE_WS" }, JsonOptions);

        var response = await _client.PostAsJsonAsync("/roles",
            new { name = "B", code = "  ROLE_WS  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/roles", new { name = "A", code = "ROLE_CA" }, JsonOptions);
        var b = await _client.PostAsJsonAsync("/roles", new { name = "B", code = "ROLE_CB" }, JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/roles/{dtoB.Id}",
            new { name = "B2", code = "  ROLE_CA  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        const string code = "ROLE_CONCURRENT";
        var bodyA = new { name = "A", code };
        var bodyB = new { name = "B", code };
        var t1 = _client.PostAsJsonAsync("/roles", bodyA, JsonOptions);
        var t2 = _client.PostAsJsonAsync("/roles", bodyB, JsonOptions);
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
        var response = await _client.PostAsJsonAsync("/roles", new { name = "   ", code = "ROLE_X1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyCode_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/roles", new { name = "N1", code = "\t  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/roles", new { name = "S", code = "ROLE_W1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/roles/{dto.Id}",
            new { name = "   ", code = "ROLE_W1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoles()
    {
        await _client.PostAsJsonAsync("/roles", new { name = "Ativo", code = "ROLE_ATIVO" }, JsonOptions);

        var other = await _client.PostAsJsonAsync("/roles", new { name = "Outro", code = "ROLE_OUTRO" }, JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/roles/{toDelete.Id}");

        var listResp = await _client.GetAsync("/roles");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RoleDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Null(r.DeletedAt));
        Assert.Contains(list, r => r.Code == "ROLE_ATIVO");
        Assert.DoesNotContain(list, r => r.Code == "ROLE_OUTRO");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/roles", new { name = "S", code = "ROLE_S1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/roles/{dto.Id}");
        var get404 = await _client.GetAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/roles", new { name = "S", code = "ROLE_U1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/roles/{dto.Id}",
            new { name = "S2", code = "ROLE_U1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/roles/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/roles/{dto.Id}",
            new { name = "S3", code = "ROLE_U1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/roles", new { name = "S", code = "ROLE_D1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Roles.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/roles", new { name = "S", code = "ROLE_R1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/roles/{dto.Id}");

        var get404 = await _client.GetAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/roles/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RoleDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    private sealed record RoleDto(
        Guid Id,
        string Name,
        string Code,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
