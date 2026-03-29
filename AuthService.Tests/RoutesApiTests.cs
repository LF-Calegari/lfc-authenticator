using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RoutesApiTests : IAsyncLifetime
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

    private async Task<Guid> CreateSystemAsync(string code, string name = "Sistema")
    {
        var r = await _client.PostAsJsonAsync("/systems", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<SystemRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object RouteCreateBody(Guid systemId, string name, string code, string? description = null) =>
        new { systemId, name, code, description };

    private static object RouteUpdateBody(Guid systemId, string name, string code, string? description = null) =>
        new { systemId, name, code, description };

    [Fact]
    public async Task Create_Post_WithValidSystemId_ReturnsCreated_UtcAndNullDeletedAt()
    {
        var sysId = await CreateSystemAsync("RT_SYS_1");

        var response = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "Rota A", "ROUTE_A", "Opcional"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(sysId, dto.SystemId);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("ROUTE_A", dto.Code);
        Assert.Equal("Opcional", dto.Description);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_WithInvalidSystemId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(Guid.NewGuid(), "X", "CODE_X"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DUP");
        await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "A", "DUP_CODE"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "B", "DUP_CODE"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WS");
        await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "A", "ABC_RT"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "B", "  ABC_RT  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_CONC");
        const string code = "CONC_RT";
        var t1 = _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "A", code), TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "B", code), TestApiClient.JsonOptions);
        await Task.WhenAll(t1, t2);
        var statuses = new[] { (await t1).StatusCode, (await t2).StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WN");
        var response = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "   ", "C1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoutes()
    {
        var sysId = await CreateSystemAsync("RT_SYS_GA");
        await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "Ativa", "RT_ATIVA"), TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "Outra", "RT_OUTRA"), TestApiClient.JsonOptions);
        var otherDto = await other.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(otherDto);
        await _client.DeleteAsync($"/systems/routes/{otherDto.Id}");

        var listResp = await _client.GetAsync("/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Null(r.DeletedAt));
        Assert.Contains(list, r => r.Code == "RT_ATIVA");
        Assert.DoesNotContain(list, r => r.Code == "RT_OUTRA");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_G1");
        var create = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "S", "RT_G1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/systems/routes/{dto.Id}")).StatusCode);

        await _client.DeleteAsync($"/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_U1");
        var create = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "S", "RT_U1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_U1", null), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/systems/routes/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S3", "RT_U1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UINV");
        var create = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "S", "RT_INV"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/systems/routes/{dto.Id}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_INV"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentId_WithInvalidSystemId_Returns404()
    {
        var missingId = Guid.NewGuid();
        var put = await _client.PutAsJsonAsync($"/systems/routes/{missingId}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_NO404"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_D1");
        var create = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "S", "RT_D1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/systems/routes/{dto.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Routes.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenAppearsInGetAll_AndGetByIdWorks()
    {
        var sysId = await CreateSystemAsync("RT_SYS_R1");
        var create = await _client.PostAsJsonAsync("/systems/routes", RouteCreateBody(sysId, "S", "RT_R1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/systems/routes/{dto.Id}")).StatusCode);

        var restore = await _client.PostAsync($"/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);

        var listResp = await _client.GetAsync("/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Create_WithDeletedSystem_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DELSYS");
        await _client.DeleteAsync($"/systems/{sysId}");

        var response = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "R", "RT_DELSYS"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeRoutesWhenSystemSoftDeleted()
    {
        var sysId = await CreateSystemAsync("RT_SYS_ORPH");
        var create = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "R", "RT_ORPH"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/systems/{sysId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/systems/routes/{dto.Id}")).StatusCode);

        var listResp = await _client.GetAsync("/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Restore_WhenSystemSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_RSYS");
        var create = await _client.PostAsJsonAsync("/systems/routes",
            RouteCreateBody(sysId, "S", "RT_RSYS"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/systems/routes/{dto.Id}");
        await _client.DeleteAsync($"/systems/{sysId}");

        var restore = await _client.PostAsync($"/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    private sealed record SystemRefDto(Guid Id);

    private sealed record RouteDto(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
