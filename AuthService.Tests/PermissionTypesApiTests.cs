using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class PermissionTypesApiTests : IAsyncLifetime
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
        var body = new { name = "Tipo X", code = "PT_X", description = "Opcional" };
        var response = await _client.PostAsJsonAsync("/permission-types", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Null(dto.DeletedAt);
        Assert.Equal("Tipo X", dto.Name);
        Assert.Equal("PT_X", dto.Code);
        Assert.Equal("Opcional", dto.Description);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/permission-types", new { name = "A", code = "PT_ABC" }, JsonOptions);

        var response = await _client.PostAsJsonAsync("/permission-types", new { name = "B", code = "PT_ABC" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/permission-types", new { name = "A", code = "PT_WS" }, JsonOptions);

        var response = await _client.PostAsJsonAsync("/permission-types",
            new { name = "B", code = "  PT_WS  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCode_NormalizesWhitespace_ReturnsConflict()
    {
        await _client.PostAsJsonAsync("/permission-types", new { name = "A", code = "PT_CA" }, JsonOptions);
        var b = await _client.PostAsJsonAsync("/permission-types", new { name = "B", code = "PT_CB" }, JsonOptions);
        var dtoB = await b.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dtoB);

        var put = await _client.PutAsJsonAsync($"/permission-types/{dtoB.Id}",
            new { name = "B2", code = "  PT_CA  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameCode_OneCreatedOneConflict()
    {
        const string code = "PT_CONCURRENT";
        var bodyA = new { name = "A", code };
        var bodyB = new { name = "B", code };
        var t1 = _client.PostAsJsonAsync("/permission-types", bodyA, JsonOptions);
        var t2 = _client.PostAsJsonAsync("/permission-types", bodyB, JsonOptions);
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
        var response = await _client.PostAsJsonAsync("/permission-types", new { name = "   ", code = "PT_X1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceOnlyCode_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/permission-types", new { name = "N1", code = "\t  " }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WhitespaceOnlyName_ReturnsBadRequest()
    {
        var create = await _client.PostAsJsonAsync("/permission-types", new { name = "S", code = "PT_W1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/permission-types/{dto.Id}",
            new { name = "   ", code = "PT_W1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActivePermissionTypes()
    {
        await _client.PostAsJsonAsync("/permission-types", new { name = "Ativo", code = "PT_ATIVO" }, JsonOptions);

        var other = await _client.PostAsJsonAsync("/permission-types", new { name = "Outro", code = "PT_OUTRO" }, JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/permission-types/{toDelete.Id}");

        var listResp = await _client.GetAsync("/permission-types");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<PermissionTypeDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, p => Assert.Null(p.DeletedAt));
        Assert.Contains(list, p => p.Code == "PT_ATIVO");
        Assert.DoesNotContain(list, p => p.Code == "PT_OUTRO");
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/permission-types", new { name = "S", code = "PT_S1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);

        var getOk = await _client.GetAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);

        await _client.DeleteAsync($"/permission-types/{dto.Id}");
        var get404 = await _client.GetAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/permission-types", new { name = "S", code = "PT_U1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/permission-types/{dto.Id}",
            new { name = "S2", code = "PT_U1", description = (string?)null }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/permission-types/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/permission-types/{dto.Id}",
            new { name = "S3", code = "PT_U1" }, JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/permission-types", new { name = "S", code = "PT_D1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);

        var del1 = await _client.DeleteAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PermissionTypes.IgnoreQueryFilters().SingleAsync(p => p.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        var del2 = await _client.DeleteAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenOperationsWorkAgain()
    {
        var create = await _client.PostAsJsonAsync("/permission-types", new { name = "S", code = "PT_R1" }, JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/permission-types/{dto.Id}");

        var get404 = await _client.GetAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/permission-types/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/permission-types/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<PermissionTypeDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    private sealed record PermissionTypeDto(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
