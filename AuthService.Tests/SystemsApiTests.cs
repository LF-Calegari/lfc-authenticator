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
        var page = await listResp.Content.ReadFromJsonAsync<PagedSystemsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.All(page.Data, s => Assert.Null(s.DeletedAt));
        Assert.Contains(page.Data, s => s.Code == "ATIVO");
        Assert.DoesNotContain(page.Data, s => s.Code == "OUTRO");
        Assert.Equal(page.Data.Count, page.Total);
    }

    [Fact]
    public async Task GetAll_NoFilters_UsesDefaultEnvelope()
    {
        var page = await GetSystemsPageAsync("/api/v1/systems");
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.Total >= page.Data.Count);
    }

    [Fact]
    public async Task GetAll_WithSearchQ_PartialMatchOnName()
    {
        await SeedSystemsAsync(("Admin GUI", "ADMIN_GUI"), ("Faturamento", "FAT_001"));

        var page = await GetSystemsPageAsync("/api/v1/systems?q=adm");
        Assert.Contains(page.Data, s => s.Code == "ADMIN_GUI");
        Assert.DoesNotContain(page.Data, s => s.Code == "FAT_001");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_IsCaseInsensitive()
    {
        await SeedSystemsAsync(("Admin GUI", "ADMIN_GUI"));

        var lower = await GetSystemsPageAsync("/api/v1/systems?q=admin");
        var upper = await GetSystemsPageAsync("/api/v1/systems?q=ADMIN");
        var mixed = await GetSystemsPageAsync("/api/v1/systems?q=AdMiN");

        Assert.Contains(lower.Data, s => s.Code == "ADMIN_GUI");
        Assert.Contains(upper.Data, s => s.Code == "ADMIN_GUI");
        Assert.Contains(mixed.Data, s => s.Code == "ADMIN_GUI");
    }

    [Fact]
    public async Task GetAll_WithSearchQ_MatchesNameOrCode()
    {
        await SeedSystemsAsync(
            ("Sistema Alpha", "BETA_CODE"),
            ("Outro Nome", "ALPHA_CODE"),
            ("Sem relacao", "NEUTRO"));

        var byName = await GetSystemsPageAsync("/api/v1/systems?q=Alpha");
        Assert.Contains(byName.Data, s => s.Code == "BETA_CODE");
        Assert.Contains(byName.Data, s => s.Code == "ALPHA_CODE");
        Assert.DoesNotContain(byName.Data, s => s.Code == "NEUTRO");
    }

    [Fact]
    public async Task GetAll_WithPagination_ReturnsCorrectSubset()
    {
        await SeedSystemsAsync(
            ("AAA Sistema", "PAGE_AAA"),
            ("BBB Sistema", "PAGE_BBB"),
            ("CCC Sistema", "PAGE_CCC"),
            ("DDD Sistema", "PAGE_DDD"),
            ("EEE Sistema", "PAGE_EEE"));

        var firstPage = await GetSystemsPageAsync("/api/v1/systems?q=Sistema&page=1&pageSize=2");
        var secondPage = await GetSystemsPageAsync("/api/v1/systems?q=Sistema&page=2&pageSize=2");
        var thirdPage = await GetSystemsPageAsync("/api/v1/systems?q=Sistema&page=3&pageSize=2");

        Assert.Equal(2, firstPage.Data.Count);
        Assert.Equal(2, secondPage.Data.Count);
        Assert.Single(thirdPage.Data);
        Assert.Equal(5, firstPage.Total);
        Assert.Equal(5, secondPage.Total);
        Assert.Equal(5, thirdPage.Total);

        var firstCodes = firstPage.Data.Select(s => s.Code).ToList();
        var secondCodes = secondPage.Data.Select(s => s.Code).ToList();
        var thirdCodes = thirdPage.Data.Select(s => s.Code).ToList();
        Assert.Empty(firstCodes.Intersect(secondCodes));
        Assert.Empty(secondCodes.Intersect(thirdCodes));
        Assert.Empty(firstCodes.Intersect(thirdCodes));
    }

    [Fact]
    public async Task GetAll_WithPageSizeAboveLimit_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageSizeNegative_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems?pageSize=-3");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageZero_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithPageNegative_ReturnsBadRequest()
    {
        var resp = await _client.GetAsync("/api/v1/systems?page=-1");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedTrue_ReturnsActiveAndDeleted()
    {
        await SeedSystemsAsync(("Sis Ativo", "INC_ACTIVE"));
        var deletedResp = await _client.PostAsJsonAsync(
            "/api/v1/systems",
            new { name = "Sis Deletado", code = "INC_DELETED" },
            TestApiClient.JsonOptions);
        var deleted = await deletedResp.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(deleted);
        await _client.DeleteAsync($"/api/v1/systems/{deleted.Id}");

        var page = await GetSystemsPageAsync("/api/v1/systems?includeDeleted=true&q=Sis&pageSize=100");
        Assert.Contains(page.Data, s => s.Code == "INC_ACTIVE");
        Assert.Contains(page.Data, s => s.Code == "INC_DELETED" && s.DeletedAt != null);
    }

    [Fact]
    public async Task GetAll_WithIncludeDeletedDefault_HidesSoftDeleted()
    {
        var resp = await _client.PostAsJsonAsync(
            "/api/v1/systems",
            new { name = "Sis Hidden", code = "HIDE_DELETED" },
            TestApiClient.JsonOptions);
        var dto = await resp.Content.ReadFromJsonAsync<SystemDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        await _client.DeleteAsync($"/api/v1/systems/{dto.Id}");

        var page = await GetSystemsPageAsync("/api/v1/systems?q=Hidden&pageSize=100");
        Assert.DoesNotContain(page.Data, s => s.Code == "HIDE_DELETED");
    }

    [Fact]
    public async Task GetAll_TotalReflectsFilters_BeforePagination()
    {
        await SeedSystemsAsync(
            ("Filtro UM", "FIL_TOTAL_1"),
            ("Filtro DOIS", "FIL_TOTAL_2"),
            ("Filtro TRES", "FIL_TOTAL_3"),
            ("Outro", "OTHER_TOTAL"));

        var page = await GetSystemsPageAsync("/api/v1/systems?q=Filtro&page=1&pageSize=2");
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Data.Count);
    }

    [Fact]
    public async Task GetAll_PageBeyondTotal_ReturnsEmptyDataAnd200()
    {
        await SeedSystemsAsync(("Empty page A", "EMPTY_A"));

        var resp = await _client.GetAsync("/api/v1/systems?q=Empty&page=99&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PagedSystemsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page.Data);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetAll_OrderingIsStable_NoDuplicatesAcrossPages()
    {
        var seeds = Enumerable.Range(0, 7)
            .Select(i => ($"Ordem {i:D2}", $"ORDER_{i:D2}"))
            .ToArray();
        await SeedSystemsAsync(seeds);

        var first = await GetSystemsPageAsync("/api/v1/systems?q=Ordem&page=1&pageSize=3");
        var second = await GetSystemsPageAsync("/api/v1/systems?q=Ordem&page=2&pageSize=3");
        var third = await GetSystemsPageAsync("/api/v1/systems?q=Ordem&page=3&pageSize=3");

        var collected = first.Data.Concat(second.Data).Concat(third.Data)
            .Select(s => s.Code)
            .ToList();
        Assert.Equal(collected.Count, collected.Distinct().Count());
        Assert.Equal(seeds.Length, collected.Count);
    }

    [Fact]
    public async Task GetAll_OrderedByName_Ascending()
    {
        await SeedSystemsAsync(
            ("ZZZ Order", "ORD_ZZZ"),
            ("AAA Order", "ORD_AAA"),
            ("MMM Order", "ORD_MMM"));

        var page = await GetSystemsPageAsync("/api/v1/systems?q=Order&pageSize=10");
        var names = page.Data.Where(s => s.Code.StartsWith("ORD_", StringComparison.Ordinal))
            .Select(s => s.Name)
            .ToList();
        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, names);
    }

    private async Task<PagedSystemsDto> GetSystemsPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedSystemsDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    private async Task SeedSystemsAsync(params (string Name, string Code)[] systems)
    {
        foreach (var (name, code) in systems)
        {
            var resp = await _client.PostAsJsonAsync(
                "/api/v1/systems",
                new { name, code },
                TestApiClient.JsonOptions);
            resp.EnsureSuccessStatusCode();
        }
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

    private sealed record PagedSystemsDto(
        List<SystemDto> Data,
        int Page,
        int PageSize,
        int Total);
}
