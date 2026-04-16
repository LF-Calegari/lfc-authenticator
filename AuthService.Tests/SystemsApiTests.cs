using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

/// <summary>
/// Cada teste usa um <see cref="WebAppFactory"/> novo → um banco PostgreSQL dedicado (criado no ctor, drop no Dispose).
/// </summary>
public class SystemsApiTests : IAsyncLifetime
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
        var body = new { name = "Sistema X", code = "SISTEMA_X", description = "Opcional" };
        var response = await _client.PostAsJsonAsync("/api/v1/systems", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Sistema X", dto.Name);
        Assert.Equal("SISTEMA_X", dto.Code);
        Assert.Equal("Opcional", dto.Description);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/systems", new { name = "A", code = "ABC" }, TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "B", code = "ABC" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/systems", new { name = "A", code = "ABC" }, TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "B", code = "  ABC  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/api/v1/systems", new { name = "A", code = "CODE_A" }, TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "B", code = "CODE_B" }, TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/{dtoB.Id}",
            new { name = "B2", code = "  CODE_A  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        const string code = "CONCURRENT_X";
        var bodyA = new { name = "A", code };
        var bodyB = new { name = "B", code };
        var t1 = _client.PostAsJsonAsync("/api/v1/systems", bodyA, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/api/v1/systems", bodyB, TestApiClient.JsonOptions);
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
        var response = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "   ", code = "X1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyCode_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "N1", code = "\t  " }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "S", code = "W1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/{dto.Id}",
            new { name = "   ", code = "W1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveSystems()
    {
        await _client.PostAsJsonAsync("/api/v1/systems", new { name = "Ativo", code = "ATIVO" }, TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "Outro", code = "OUTRO" }, TestApiClient.JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/api/v1/systems/{toDelete.Id}");

        var listResp = await _client.GetAsync("/api/v1/systems");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<SystemDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, s => Assert.Null(s.DeletedAt));
        Assert.Contains(list, s => s.Code == "ATIVO");
        Assert.DoesNotContain(list, s => s.Code == "OUTRO");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "S", code = "S1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");
        var get404 = await _client.GetAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "S", code = "U1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/systems/{dto.Id}",
            new { name = "S2", code = "U1", description = (string?)null }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/systems/{dto.Id}",
            new { name = "S3", code = "U1" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "S", code = "D1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Systems.IgnoreQueryFilters().SingleAsync(s => s.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/systems", new { name = "S", code = "R1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");

        var get404 = await _client.GetAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var restore = await _client.PostAsync($"/api/v1/systems/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/systems/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    private sealed record SystemDto(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
