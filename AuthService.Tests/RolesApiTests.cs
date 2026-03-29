using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RolesApiTests : IAsyncLifetime
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

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithUtcTimestamps_AndNullDeletedAt()
    {
        var body = new { name = "Administrador", code = "ROLE_ADMIN" };
        var response = await _client.PostAsJsonAsync("/v1/roles", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
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
        await _client.PostAsJsonAsync("/v1/roles", new { name = "A", code = "ROLE_ABC" }, TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/v1/roles", new { name = "B", code = "ROLE_ABC" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/v1/roles", new { name = "A", code = "ROLE_WS" }, TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/v1/roles",
            new { name = "B", code = "  ROLE_WS  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/v1/roles", new { name = "A", code = "ROLE_CA" }, TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/v1/roles", new { name = "B", code = "ROLE_CB" }, TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/v1/roles/{dtoB.Id}",
            new { name = "B2", code = "  ROLE_CA  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        const string code = "ROLE_CONCURRENT";
        var bodyA = new { name = "A", code };
        var bodyB = new { name = "B", code };
        var t1 = _client.PostAsJsonAsync("/v1/roles", bodyA, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/v1/roles", bodyB, TestApiClient.JsonOptions);
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
        var response = await _client.PostAsJsonAsync("/v1/roles", new { name = "   ", code = "ROLE_X1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyCode_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/v1/roles", new { name = "N1", code = "\t  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NameExceedsMaxLength_ReturnsBadRequest()
    {
        var name81 = new string('n', 81);
        var response = await _client.PostAsJsonAsync("/v1/roles",
            new { name = name81, code = "ROLE_LEN_NAME" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_CodeExceedsMaxLength_ReturnsBadRequest()
    {
        var code51 = new string('c', 51);
        var response = await _client.PostAsJsonAsync("/v1/roles",
            new { name = "Nome válido", code = code51 }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Restore só aplica a linhas soft-deleted; role ativo não é encontrado pela query e retorna 404.
    /// </summary>
    [Fact]
    public async Task Restore_ActiveRole_ReturnsNotFound()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "Ativo", code = "ROLE_ACTIVE_R" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var restore = await _client.PostAsync($"/v1/roles/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "S", code = "ROLE_W1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/v1/roles/{dto.Id}",
            new { name = "   ", code = "ROLE_W1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoles()
    {
        await _client.PostAsJsonAsync("/v1/roles", new { name = "Ativo", code = "ROLE_ATIVO" }, TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/v1/roles", new { name = "Outro", code = "ROLE_OUTRO" }, TestApiClient.JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/v1/roles/{toDelete.Id}");

        var listResp = await _client.GetAsync("/v1/roles");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RoleDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Null(r.DeletedAt));
        Assert.Contains(list, r => r.Code == "ROLE_ATIVO");
        Assert.DoesNotContain(list, r => r.Code == "ROLE_OUTRO");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "S", code = "ROLE_S1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/v1/roles/{dto.Id}");
        var get404 = await _client.GetAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "S", code = "ROLE_U1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/v1/roles/{dto.Id}",
            new { name = "S2", code = "ROLE_U1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/v1/roles/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/v1/roles/{dto.Id}",
            new { name = "S3", code = "ROLE_U1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "S", code = "ROLE_D1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Roles.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/v1/roles", new { name = "S", code = "ROLE_R1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/roles/{dto.Id}");

        var get404 = await _client.GetAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var restore = await _client.PostAsync($"/v1/roles/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
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
