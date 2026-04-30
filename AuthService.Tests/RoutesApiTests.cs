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
        var r = await _client.PostAsJsonAsync("/api/v1/systems", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<SystemRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> GetDefaultSystemTokenTypeIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.SystemTokenTypes
            .Where(t => t.Code == SystemTokenTypeSeeder.DefaultCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();
        Assert.True(id.HasValue && id.Value != Guid.Empty);
        return id!.Value;
    }

    private async Task<Guid> CreateActiveSystemTokenTypeAsync(string code, string name = "Custom")
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/tokens/types", new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<TokenTypeRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object RouteCreateBody(Guid systemId, string name, string code, Guid systemTokenTypeId, string? description = null) =>
        new { systemId, name, code, description, systemTokenTypeId };

    private static object RouteUpdateBody(Guid systemId, string name, string code, Guid systemTokenTypeId, string? description = null) =>
        new { systemId, name, code, description, systemTokenTypeId };

    [Fact]
    public async Task Create_Post_WithValidSystemId_ReturnsCreated_UtcAndNullDeletedAt()
    {
        var sysId = await CreateSystemAsync("RT_SYS_1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota A", "ROUTE_A", sttId, "Opcional"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(sysId, dto.SystemId);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("ROUTE_A", dto.Code);
        Assert.Equal("Opcional", dto.Description);
        Assert.Equal(sttId, dto.SystemTokenTypeId);
        Assert.Equal(SystemTokenTypeSeeder.DefaultCode, dto.SystemTokenTypeCode);
        Assert.False(string.IsNullOrWhiteSpace(dto.SystemTokenTypeName));
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(Guid.NewGuid(), "X", "CODE_X", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutSystemId_ReturnsBadRequest()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { name = "Rota sem sistema", code = "RT_NO_SYS", systemTokenTypeId = sttId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_NO_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { systemId = sysId, name = "Rota sem stt", code = "RT_NO_STT" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptySystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_EMPTY_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota empty stt", "RT_EMPTY_STT", Guid.Empty), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_INV_STT");
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota inv stt", "RT_INV_STT", Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInactiveSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DEL_STT");
        var customId = await CreateActiveSystemTokenTypeAsync("STT_DEL_FOR_RT");

        var del = await _client.DeleteAsync($"/api/v1/tokens/types/{customId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "Rota stt deletado", "RT_DEL_STT", customId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DUP");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", "DUP_CODE", sttId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "B", "DUP_CODE", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", "ABC_RT", sttId), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "B", "  ABC_RT  ", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        var sysId = await CreateSystemAsync("RT_SYS_CONC");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        const string code = "CONC_RT";
        var t1 = _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "A", code, sttId), TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "B", code, sttId), TestApiClient.JsonOptions);
        await Task.WhenAll(t1, t2);
        var statuses = new[] { (await t1).StatusCode, (await t2).StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_WN");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "   ", "C1", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoutes_AndPopulatesDenormalizedTokenTypeFields()
    {
        var sysId = await CreateSystemAsync("RT_SYS_GA");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "Ativa", "RT_ATIVA", sttId), TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "Outra", "RT_OUTRA", sttId), TestApiClient.JsonOptions);
        var otherDto = await other.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(otherDto);
        await _client.DeleteAsync($"/api/v1/systems/routes/{otherDto.Id}");

        var listResp = await _client.GetAsync("/api/v1/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Null(r.DeletedAt));
        Assert.All(list, r => Assert.False(string.IsNullOrWhiteSpace(r.SystemTokenTypeCode)));
        Assert.All(list, r => Assert.False(string.IsNullOrWhiteSpace(r.SystemTokenTypeName)));
        Assert.Contains(list, r => r.Code == "RT_ATIVA");
        Assert.DoesNotContain(list, r => r.Code == "RT_OUTRA");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_G1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_G1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOkResp = await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOkResp.StatusCode);
        var getOk = await getOkResp.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(getOk);
        Assert.Equal(sttId, getOk.SystemTokenTypeId);
        Assert.Equal(SystemTokenTypeSeeder.DefaultCode, getOk.SystemTokenTypeCode);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_U1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_U1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_U1", sttId, null), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S3", "RT_U1", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UINV");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_INV", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_INV", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_MISSING");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_MISSING", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            new { name = "S atualizado", code = "RT_MISSING", systemTokenTypeId = sttId }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_MISSING_STT");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_MISS_STT", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            new { systemId = sysId, name = "S2", code = "RT_MISS_STT" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithUnknownSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UPD_INV_STT");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_UPD_INV_STT", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_UPD_INV_STT", Guid.NewGuid()), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_WithInactiveSystemTokenTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_UPD_DEL_STT");
        var defaultSttId = await GetDefaultSystemTokenTypeIdAsync();
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_UPD_DEL");

        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_UPD_DEL_STT", defaultSttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del = await _client.DeleteAsync($"/api/v1/tokens/types/{customSttId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{dto.Id}",
            RouteUpdateBody(sysId, "S2", "RT_UPD_DEL_STT", customSttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentId_WithInvalidSystemId_Returns404()
    {
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var missingId = Guid.NewGuid();
        var put = await _client.PutAsJsonAsync($"/api/v1/systems/routes/{missingId}",
            RouteUpdateBody(Guid.NewGuid(), "S", "RT_NO404", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var sysId = await CreateSystemAsync("RT_SYS_D1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_D1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Routes.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenAppearsInGetAll_AndGetByIdWorks()
    {
        var sysId = await CreateSystemAsync("RT_SYS_R1");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes", RouteCreateBody(sysId, "S", "RT_R1", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);

        var listResp = await _client.GetAsync("/api/v1/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Create_WithDeletedSystem_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_DELSYS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        var response = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "R", "RT_DELSYS", sttId), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeRoutesWhenSystemSoftDeleted()
    {
        var sysId = await CreateSystemAsync("RT_SYS_ORPH");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "R", "RT_ORPH", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);

        var listResp = await _client.GetAsync("/api/v1/systems/routes");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RouteDto>>(TestApiClient.JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, r => r.Id == dto.Id);
    }

    [Fact]
    public async Task Restore_WhenSystemSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_RSYS");
        var sttId = await GetDefaultSystemTokenTypeIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "S", "RT_RSYS", sttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}");
        await _client.DeleteAsync($"/api/v1/systems/{sysId}");

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    [Fact]
    public async Task Restore_WhenSystemTokenTypeSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("RT_SYS_RSTT");
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_RSTT");

        var create = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            RouteCreateBody(sysId, "S", "RT_RSTT", customSttId), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RouteDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        // soft-delete da rota
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/systems/routes/{dto.Id}")).StatusCode);
        // soft-delete do SystemTokenType referenciado
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/tokens/types/{customSttId}")).StatusCode);

        var restore = await _client.PostAsync($"/api/v1/systems/routes/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    [Fact]
    public async Task Sync_WithoutSystemTokenTypeCode_DefaultsToCanonicalDefault()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_DEF", "Sistema sync default");
        var defaultSttId = await GetDefaultSystemTokenTypeIdAsync();

        var body = new
        {
            systemCode = "RT_SYNC_DEF",
            routes = new[]
            {
                new { code = "SYNC_DEF_RT", name = "Rota sync default", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = (string?)null }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.SingleAsync(r => r.Code == "SYNC_DEF_RT");
        Assert.Equal(defaultSttId, route.SystemTokenTypeId);
    }

    [Fact]
    public async Task Sync_WithUnknownSystemTokenTypeCode_ReturnsBadRequest_AndListsUnknownCodes()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_UNK_STT", "Sistema sync stt unknown");

        var body = new
        {
            systemCode = "RT_SYNC_UNK_STT",
            routes = new[]
            {
                new { code = "SYNC_UNK_STT_RT", name = "Rota stt unknown", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = "stt-nope-1" }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.Contains("stt-nope-1", content);
    }

    [Fact]
    public async Task Sync_WithKnownSystemTokenTypeCode_PersistsOnRoute()
    {
        var sysId = await CreateSystemAsync("RT_SYNC_KNOWN_STT", "Sistema sync stt known");
        var customSttId = await CreateActiveSystemTokenTypeAsync("STT_SYNC_KNOWN");

        var body = new
        {
            systemCode = "RT_SYNC_KNOWN_STT",
            routes = new[]
            {
                new { code = "SYNC_KNOWN_STT_RT", name = "Rota stt known", description = (string?)null, permissionTypeCode = (string?)null, systemTokenTypeCode = "STT_SYNC_KNOWN" }
            }
        };

        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes/sync", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var route = await db.Routes.SingleAsync(r => r.Code == "SYNC_KNOWN_STT_RT");
        Assert.Equal(customSttId, route.SystemTokenTypeId);
    }

    private sealed record SystemRefDto(Guid Id);
    private sealed record TokenTypeRefDto(Guid Id);

    private sealed record RouteDto(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        Guid SystemTokenTypeId,
        string SystemTokenTypeCode,
        string SystemTokenTypeName,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
