using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class TokenTypesApiTests : IAsyncLifetime
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
        var body = new { name = "Tipo token", code = "TT_X", description = "Opcional" };
        var response = await _client.PostAsJsonAsync("/v1/tokens/types", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<TokenTypeDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Tipo token", dto.Name);
        Assert.Equal("TT_X", dto.Code);
        Assert.Equal("Opcional", dto.Description);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/v1/tokens/types", new { name = "A", code = "TT_DUP" }, TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/v1/tokens/types", new { name = "B", code = "TT_DUP" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var anon = _factory.CreateClient();
        var response = await anon.GetAsync("/v1/tokens/types");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetReturnsOk()
    {
        var create = await _client.PostAsJsonAsync("/v1/tokens/types", new { name = "S", code = "TT_R1" }, TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<TokenTypeDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/v1/tokens/types/{dto.Id}");

        var restore = await _client.PostAsync($"/v1/tokens/types/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/v1/tokens/types/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SystemTokenTypes.IgnoreQueryFilters().SingleAsync(t => t.Id == dto.Id);
            Assert.Null(row.DeletedAt);
        }
    }

    private sealed record TokenTypeDto(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
