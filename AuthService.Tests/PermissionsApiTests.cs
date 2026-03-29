using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class PermissionsApiTests : IAsyncLifetime
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

    private async Task<Guid> CreateSystemAsync(string code, string name = "Sistema")
    {
        var r = await _client.PostAsJsonAsync("/systems", new { name, code, description = (string?)null }, JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefDto>(JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionTypeAsync(string code, string name = "Tipo")
    {
        var r = await _client.PostAsJsonAsync("/permission-types", new { name, code, description = (string?)null }, JsonOptions);
        r.EnsureSuccessStatusCode();
        var dto = await r.Content.ReadFromJsonAsync<RefDto>(JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private static object PermCreateBody(Guid systemId, Guid permissionTypeId, string? description = null) =>
        new { systemId, permissionTypeId, description };

    private static object PermUpdateBody(Guid systemId, Guid permissionTypeId, string? description = null) =>
        new { systemId, permissionTypeId, description };

    [Fact]
    public async Task Create_Post_WithValidReferences_ReturnsCreated_UtcAndNullDeletedAt()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_1");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, typeId, "Opcional"), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(sysId, dto.SystemId);
        Assert.Equal(typeId, dto.PermissionTypeId);
        Assert.Equal("Opcional", dto.Description);
        Assert.Null(dto.DeletedAt);
        Assert.True(dto.CreatedAt > DateTime.MinValue);
        Assert.True(dto.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_WithoutDescription_NormalizesToNull()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_ND");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_ND");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, typeId, "   "), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task Create_WithInvalidSystemId_ReturnsBadRequest()
    {
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_INV_S");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(Guid.NewGuid(), typeId), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidPermissionTypeId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_INV_T");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, Guid.NewGuid()), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DescriptionExceedsMaxLength_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_LEN");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_LEN");
        var longDesc = new string('d', 501);

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, typeId, longDesc), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restore_ActivePermission_ReturnsNotFound()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_AR");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_AR");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActivePermissions()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_GA");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_GA");
        await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId, null), JsonOptions);

        var other = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId, "b"), JsonOptions);
        var toDelete = await other.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(toDelete);
        await _client.DeleteAsync($"/permissions/{toDelete.Id}");

        var listResp = await _client.GetAsync("/permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<PermissionDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list, p => Assert.Null(p.DeletedAt));
        Assert.Single(list);
    }

    [Fact]
    public async Task GetById_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_G1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_G1");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/permissions/{dto.Id}")).StatusCode);

        await _client.DeleteAsync($"/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/permissions/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_Active_ReturnsOk_Deleted_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_U1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_U1");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId, "a"), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        var putOk = await _client.PutAsJsonAsync($"/permissions/{dto.Id}",
            PermUpdateBody(sysId, typeId, "b"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);

        await _client.DeleteAsync($"/permissions/{dto.Id}");
        var put404 = await _client.PutAsJsonAsync($"/permissions/{dto.Id}",
            PermUpdateBody(sysId, typeId, "c"), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, put404.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidSystemId_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_UINV");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_UINV");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        var put = await _client.PutAsJsonAsync($"/permissions/{dto.Id}",
            PermUpdateBody(Guid.NewGuid(), typeId), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDelete_ThenDeleteAgain_Returns404()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_D1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_D1");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/permissions/{dto.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Permissions.IgnoreQueryFilters().SingleAsync(p => p.Id == dto.Id);
            Assert.NotNull(row.DeletedAt);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/permissions/{dto.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_Deleted_ThenGetByIdWorks()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_R1");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_R1");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/permissions/{dto.Id}")).StatusCode);

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var getOk = await _client.GetAsync($"/permissions/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOk.StatusCode);
        var restored = await getOk.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Create_WithDeletedSystem_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_DELSYS");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_DELSYS");
        await _client.DeleteAsync($"/systems/{sysId}");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, typeId), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeWhenSystemSoftDeleted()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_ORPH");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_ORPH");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/systems/{sysId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/permissions/{dto.Id}")).StatusCode);

        var listResp = await _client.GetAsync("/permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<PermissionDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, p => p.Id == dto.Id);
    }

    [Fact]
    public async Task GetAll_AndGetById_ExcludeWhenPermissionTypeSoftDeleted()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_PT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_PT");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/permission-types/{typeId}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/permissions/{dto.Id}")).StatusCode);

        var listResp = await _client.GetAsync("/permissions");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<PermissionDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, p => p.Id == dto.Id);
    }

    [Fact]
    public async Task Restore_WhenSystemSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_RSYS");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_RSYS");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/permissions/{dto.Id}");
        await _client.DeleteAsync($"/systems/{sysId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    [Fact]
    public async Task Create_WithDeletedPermissionType_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_DELPT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_DELPT");
        await _client.DeleteAsync($"/permission-types/{typeId}");

        var response = await _client.PostAsJsonAsync("/permissions",
            PermCreateBody(sysId, typeId), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restore_WhenPermissionTypeSoftDeleted_ReturnsBadRequest()
    {
        var sysId = await CreateSystemAsync("PERM_SYS_RPT");
        var typeId = await CreatePermissionTypeAsync("PERM_TYPE_RPT");
        var create = await _client.PostAsJsonAsync("/permissions", PermCreateBody(sysId, typeId), JsonOptions);
        var dto = await create.Content.ReadFromJsonAsync<PermissionDto>(JsonOptions);
        Assert.NotNull(dto);

        await _client.DeleteAsync($"/permissions/{dto.Id}");
        await _client.DeleteAsync($"/permission-types/{typeId}");

        var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/permissions/{dto.Id}/restore"));
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    private sealed record RefDto(Guid Id);

    private sealed record PermissionDto(
        Guid Id,
        Guid SystemId,
        Guid PermissionTypeId,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);
}
