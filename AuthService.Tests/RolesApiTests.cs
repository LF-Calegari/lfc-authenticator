using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class RolesApiTests : IAsyncLifetime
{
    private const string AuthenticatorSystemCode = "authenticator";

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

    private async Task<Guid> GetAuthenticatorSystemIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Systems
            .Where(s => s.Code == AuthenticatorSystemCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();
        Assert.True(id.HasValue && id.Value != Guid.Empty);
        return id!.Value;
    }

    private async Task<Guid> CreateSystemAsync(string code, string name = "Sistema Teste")
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/systems",
            new { name, code, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<SystemRefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> SoftDeleteSystemAsync(Guid systemId)
    {
        var resp = await _client.DeleteAsync($"/api/v1/systems/{systemId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        return systemId;
    }

    private static object RoleCreateBody(Guid systemId, string name, string code, string? description = null) =>
        new { systemId, name, code, description };

    private static object RoleUpdateBody(Guid systemId, string name, string code, string? description = null) =>
        new { systemId, name, code, description };

    [Fact]
    public async Task Create_Post_ReturnsCreated_WithUtcTimestamps_AndNullDeletedAt()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var body = RoleCreateBody(systemId, "Administrador", "ROLE_ADMIN", "Descrição opcional");
        var response = await _client.PostAsJsonAsync("/api/v1/roles", body, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(systemId, dto.SystemId);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Administrador", dto.Name);
        Assert.Equal("ROLE_ADMIN", dto.Code);
        Assert.Equal("Descrição opcional", dto.Description);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_WithoutSystemId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new { name = "Sem sistema", code = "ROLE_NO_SYS" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptySystemId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(Guid.Empty, "X", "ROLE_EMPTY_SYS"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownSystemId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(Guid.NewGuid(), "X", "ROLE_UNK_SYS"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSoftDeletedSystem_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("ROLE_SYS_DEL");
        await SoftDeleteSystemAsync(sysId);

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(sysId, "X", "ROLE_DEL_SYS"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_SameSystem_ReturnsConflict()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles", RoleCreateBody(systemId, "A", "ROLE_ABC"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "B", "ROLE_ABC"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameCode_DifferentSystems_BothSucceed()
    {
        var system1 = await GetAuthenticatorSystemIdAsync();
        var system2 = await CreateSystemAsync("ROLE_SCOPE_OTHER");

        var r1 = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system1, "Admin1", "ROLE_SHARED_CODE"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var r2 = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system2, "Admin2", "ROLE_SHARED_CODE"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

        var dto1 = await r1.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        var dto2 = await r2.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.NotEqual(dto1.Id, dto2.Id);
        Assert.Equal(system1, dto1.SystemId);
        Assert.Equal(system2, dto2.SystemId);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "A", "ROLE_WS"), TestApiClient.JsonOptions);

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "B", "  ROLE_WS  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "A", "ROLE_CA"), TestApiClient.JsonOptions);
        var b = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "B", "ROLE_CB"), TestApiClient.JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/api/v1/roles/{dtoB.Id}",
            RoleUpdateBody(systemId, "B2", "  ROLE_CA  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Update_TryingToChangeSystemId_ReturnsBadRequest()
    {
        var system1 = await GetAuthenticatorSystemIdAsync();
        var system2 = await CreateSystemAsync("ROLE_IMM_OTHER");

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system1, "Original", "ROLE_IMM"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/roles/{dto.Id}",
            RoleUpdateBody(system2, "Outro nome", "ROLE_IMM"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Update_SameSystemId_AllowsChangingNameAndDescription()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Original", "ROLE_DESC1", "antes"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/roles/{dto.Id}",
            RoleUpdateBody(systemId, "Atualizado", "ROLE_DESC1", "depois"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Atualizado", updated.Name);
        Assert.Equal("depois", updated.Description);
        Assert.Equal(systemId, updated.SystemId);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        const string code = "ROLE_CONCURRENT";
        var bodyA = RoleCreateBody(systemId, "A", code);
        var bodyB = RoleCreateBody(systemId, "B", code);
        var t1 = _client.PostAsJsonAsync("/api/v1/roles", bodyA, TestApiClient.JsonOptions);
        var t2 = _client.PostAsJsonAsync("/api/v1/roles", bodyB, TestApiClient.JsonOptions);
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
        var systemId = await GetAuthenticatorSystemIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "   ", "ROLE_X1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyCode_ReturnsBadRequest()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "N1", "\t  "), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NameExceedsMaxLength_ReturnsBadRequest()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var name81 = new string('n', 81);
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, name81, "ROLE_LEN_NAME"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_CodeExceedsMaxLength_ReturnsBadRequest()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var code51 = new string('c', 51);
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Nome válido", code51), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DescriptionExceedsMaxLength_ReturnsBadRequest()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var desc501 = new string('d', 501);
        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Nome", "ROLE_LEN_DESC", desc501), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Restore só aplica a linhas soft-deleted; role ativo não é encontrado pela query e retorna 404.
    /// </summary>
    [Fact]
    public async Task Restore_ActiveRole_ReturnsNotFound()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Ativo", "ROLE_ACTIVE_R"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var restore = await _client.PostAsync($"/api/v1/roles/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "S", "ROLE_W1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/api/v1/roles/{dto.Id}",
            RoleUpdateBody(systemId, "   ", "ROLE_W1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoFilters_UsesDefaultEnvelope()
    {
        var page = await GetRolesPageAsync("/api/v1/roles");
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.Total >= page.Data.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveRoles()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Ativo", "ROLE_ATIVO"), TestApiClient.JsonOptions);

        var other = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Outro", "ROLE_OUTRO"), TestApiClient.JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/api/v1/roles/{toDelete.Id}");

        var page = await GetRolesPageAsync("/api/v1/roles?pageSize=100");
        Assert.All(page.Data, r => Assert.Null(r.DeletedAt));
        Assert.Contains(page.Data, r => r.Code == "ROLE_ATIVO");
        Assert.DoesNotContain(page.Data, r => r.Code == "ROLE_OUTRO");
    }

    [Fact]
    public async Task GetAll_WithSystemIdFilter_ReturnsOnlyMatching()
    {
        var system1 = await GetAuthenticatorSystemIdAsync();
        var system2 = await CreateSystemAsync("ROLE_FILT_SYS");

        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system1, "S1A", "ROLE_FILT_S1A"), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system1, "S1B", "ROLE_FILT_S1B"), TestApiClient.JsonOptions);
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(system2, "S2", "ROLE_FILT_S2"), TestApiClient.JsonOptions);

        var page = await GetRolesPageAsync($"/api/v1/roles?systemId={system2}&pageSize=100");
        Assert.All(page.Data, r => Assert.Equal(system2, r.SystemId));
        Assert.Contains(page.Data, r => r.Code == "ROLE_FILT_S2");
        Assert.DoesNotContain(page.Data, r => r.Code == "ROLE_FILT_S1A");
        Assert.DoesNotContain(page.Data, r => r.Code == "ROLE_FILT_S1B");
    }

    [Fact]
    public async Task GetAll_WithEmptySystemId_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync($"/api/v1/roles?systemId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnCode()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Algo", "ROLE_QSEARCH_UNIQUE"), TestApiClient.JsonOptions);

        var page = await GetRolesPageAsync("/api/v1/roles?q=QSEARCH_UNIQUE&pageSize=100");
        Assert.Contains(page.Data, r => r.Code == "ROLE_QSEARCH_UNIQUE");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_IsCaseInsensitive()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Roberto", "ROLE_CI_TEST"), TestApiClient.JsonOptions);

        var lower = await GetRolesPageAsync("/api/v1/roles?q=roberto&pageSize=100");
        var upper = await GetRolesPageAsync("/api/v1/roles?q=ROBERTO&pageSize=100");
        Assert.Contains(lower.Data, r => r.Code == "ROLE_CI_TEST");
        Assert.Contains(upper.Data, r => r.Code == "ROLE_CI_TEST");
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsCorrectSubset()
    {
        var systemId = await CreateSystemAsync("ROLE_PG_SYS");
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/roles",
                RoleCreateBody(systemId, $"Pg {i}", $"ROLE_PAG_{i:D2}"), TestApiClient.JsonOptions);
        }

        var first = await GetRolesPageAsync($"/api/v1/roles?systemId={systemId}&page=1&pageSize=2");
        var second = await GetRolesPageAsync($"/api/v1/roles?systemId={systemId}&page=2&pageSize=2");
        var third = await GetRolesPageAsync($"/api/v1/roles?systemId={systemId}&page=3&pageSize=2");

        Assert.Equal(2, first.Data.Count);
        Assert.Equal(2, second.Data.Count);
        Assert.Single(third.Data);
        Assert.Equal(5, first.Total);
        Assert.Equal(5, second.Total);
        Assert.Equal(5, third.Total);

        var ids = first.Data.Concat(second.Data).Concat(third.Data).Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task GetAll_PageZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/roles?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_PageSizeOverMax_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/roles?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_IncludeDeletedTrue_ReturnsSoftDeletedRoles()
    {
        var systemId = await CreateSystemAsync("ROLE_DEL_SYS");
        var c = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "Para deletar", "ROLE_DEL_TARGET"), TestApiClient.JsonOptions);
        var dto = await c.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");

        var defaultPage = await GetRolesPageAsync($"/api/v1/roles?systemId={systemId}&pageSize=100");
        Assert.DoesNotContain(defaultPage.Data, r => r.Code == "ROLE_DEL_TARGET");

        var allPage = await GetRolesPageAsync($"/api/v1/roles?systemId={systemId}&includeDeleted=true&pageSize=100");
        Assert.Contains(allPage.Data, r => r.Code == "ROLE_DEL_TARGET" && r.DeletedAt is not null);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "S", "ROLE_S1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");
        var get404 = await _client.GetAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "S", "ROLE_U1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/api/v1/roles/{dto.Id}",
            RoleUpdateBody(systemId, "S2", "ROLE_U1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/api/v1/roles/{dto.Id}",
            RoleUpdateBody(systemId, "S3", "ROLE_U1"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "S", "ROLE_D1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Roles.IgnoreQueryFilters().SingleAsync(r => r.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var systemId = await GetAuthenticatorSystemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            RoleCreateBody(systemId, "S", "ROLE_R1"), TestApiClient.JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/api/v1/roles/{dto.Id}");

        var get404 = await _client.GetAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var restore = await _client.PostAsync($"/api/v1/roles/{dto.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var getOk = await _client.GetAsync($"/api/v1/roles/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    private async Task<PagedRolesDto> GetRolesPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedRolesDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private sealed record PagedRolesDto(
        List<RoleDto> Data,
        int Page,
        int PageSize,
        int Total);

    private sealed record RoleDto(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record SystemRefDto(Guid Id, string Code, string Name);
}
