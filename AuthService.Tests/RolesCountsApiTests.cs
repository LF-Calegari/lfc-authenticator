using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

/// <summary>
/// Cobre <c>permissionsCount</c>/<c>usersCount</c> denormalizados em <c>RoleResponse</c> (issue #164):
/// roles sem vínculos, com vínculos ativos, com vínculos soft-deletados (não contam) e com Permissions
/// alvo soft-deletadas (não contam). Inclui um teste com <see cref="DbCommandInterceptor"/> que valida
/// que o número de comandos SQL emitidos por <c>GET /roles</c> não cresce com a quantidade de roles
/// retornadas — garantindo que as contagens são materializadas em uma única ida ao banco (sem N+1).
/// </summary>
public class RolesCountsApiTests : IAsyncLifetime
{
    private const string AuthenticatorSystemCode = "authenticator";

    private CountingWebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new CountingWebAppFactory();
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
        var dto = await resp.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateRoleAsync(Guid systemId, string code, string name = "Role")
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/roles",
            new { systemId, name, code, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateRouteAsync(Guid systemId, string code)
    {
        var systemTokenTypeId = await GetDefaultSystemTokenTypeIdAsync();
        var resp = await _client.PostAsJsonAsync("/api/v1/systems/routes",
            new { systemId, name = "Rota", code, description = (string?)null, systemTokenTypeId },
            TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionTypeAsync(string code)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/permissions/types",
            new { name = "Tipo", code, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreatePermissionAsync(Guid routeId, Guid permissionTypeId)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/permissions",
            new { routeId, permissionTypeId, description = (string?)null }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto.Id;
    }

    private async Task<Guid> CreateUserAsync(string email, string name = "Usuário")
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/users",
            new { name, email, password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RefDto>(TestApiClient.JsonOptions);
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

    private async Task LinkRolePermissionAsync(Guid roleId, Guid permissionId)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/roles/{roleId}/permissions",
            new { permissionId }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
    }

    private async Task LinkUserRoleAsync(Guid userId, Guid roleId)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/users/{userId}/roles",
            new { roleId }, TestApiClient.JsonOptions);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<RoleDto> GetRoleByIdAsync(Guid roleId)
    {
        var resp = await _client.GetAsync($"/api/v1/roles/{roleId}");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<RoleDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        return dto;
    }

    private async Task<RoleDto> GetRoleFromListByIdAsync(Guid systemId, Guid roleId, bool includeDeleted = false)
    {
        var url = $"/api/v1/roles?systemId={systemId}&pageSize=100"
            + (includeDeleted ? "&includeDeleted=true" : string.Empty);
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<PagedRolesDto>(TestApiClient.JsonOptions);
        Assert.NotNull(page);
        var role = page.Data.SingleOrDefault(r => r.Id == roleId);
        Assert.NotNull(role);
        return role;
    }

    [Fact]
    public async Task GetById_RoleWithoutLinks_ReturnsZeroCounts()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_NL");
        var roleId = await CreateRoleAsync(systemId, "ROLE_CNT_EMPTY");

        var dto = await GetRoleByIdAsync(roleId);
        Assert.Equal(0, dto.PermissionsCount);
        Assert.Equal(0, dto.UsersCount);
    }

    [Fact]
    public async Task GetById_RoleWithActiveLinks_CountsBoth()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_AL");
        var roleId = await CreateRoleAsync(systemId, "ROLE_CNT_ACT");

        var routeId = await CreateRouteAsync(systemId, "RT_CNT_AL_1");
        var typeId = await CreatePermissionTypeAsync("PT_CNT_AL_1");
        var permA = await CreatePermissionAsync(routeId, typeId);
        var typeIdB = await CreatePermissionTypeAsync("PT_CNT_AL_2");
        var permB = await CreatePermissionAsync(routeId, typeIdB);

        await LinkRolePermissionAsync(roleId, permA);
        await LinkRolePermissionAsync(roleId, permB);

        var u1 = await CreateUserAsync("cnt.act.u1@example.com");
        var u2 = await CreateUserAsync("cnt.act.u2@example.com");
        var u3 = await CreateUserAsync("cnt.act.u3@example.com");
        await LinkUserRoleAsync(u1, roleId);
        await LinkUserRoleAsync(u2, roleId);
        await LinkUserRoleAsync(u3, roleId);

        var dto = await GetRoleByIdAsync(roleId);
        Assert.Equal(2, dto.PermissionsCount);
        Assert.Equal(3, dto.UsersCount);

        var fromList = await GetRoleFromListByIdAsync(systemId, roleId);
        Assert.Equal(2, fromList.PermissionsCount);
        Assert.Equal(3, fromList.UsersCount);
    }

    [Fact]
    public async Task GetById_RoleWithSoftDeletedLinks_DoesNotCountThem()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_SD");
        var roleId = await CreateRoleAsync(systemId, "ROLE_CNT_SDLINK");

        var routeId = await CreateRouteAsync(systemId, "RT_CNT_SD_1");
        var typeIdA = await CreatePermissionTypeAsync("PT_CNT_SD_A");
        var permA = await CreatePermissionAsync(routeId, typeIdA);
        var typeIdB = await CreatePermissionTypeAsync("PT_CNT_SD_B");
        var permB = await CreatePermissionAsync(routeId, typeIdB);

        await LinkRolePermissionAsync(roleId, permA);
        await LinkRolePermissionAsync(roleId, permB);

        // Remove um vínculo (soft-delete via DELETE /api/v1/roles/{id}/permissions/{permId}).
        var del = await _client.DeleteAsync($"/api/v1/roles/{roleId}/permissions/{permA}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var u1 = await CreateUserAsync("cnt.sd.u1@example.com");
        var u2 = await CreateUserAsync("cnt.sd.u2@example.com");
        await LinkUserRoleAsync(u1, roleId);
        await LinkUserRoleAsync(u2, roleId);

        // Soft-delete do vínculo UserRole de u2 diretamente via DbContext (não há endpoint público
        // de DELETE para UserRole por id; o DELETE de Users/{id}/roles/{roleId} usa UpdateUserRole).
        var delUserRole = await _client.DeleteAsync($"/api/v1/users/{u2}/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, delUserRole.StatusCode);

        var dto = await GetRoleByIdAsync(roleId);
        Assert.Equal(1, dto.PermissionsCount);
        Assert.Equal(1, dto.UsersCount);
    }

    [Fact]
    public async Task GetById_RoleWithSoftDeletedTargetPermission_DoesNotCountIt()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_SDT");
        var roleId = await CreateRoleAsync(systemId, "ROLE_CNT_SDTGT");

        var routeId = await CreateRouteAsync(systemId, "RT_CNT_SDT_1");
        var typeIdA = await CreatePermissionTypeAsync("PT_CNT_SDT_A");
        var permA = await CreatePermissionAsync(routeId, typeIdA);
        var typeIdB = await CreatePermissionTypeAsync("PT_CNT_SDT_B");
        var permB = await CreatePermissionAsync(routeId, typeIdB);

        await LinkRolePermissionAsync(roleId, permA);
        await LinkRolePermissionAsync(roleId, permB);

        // Soft-delete da Permission alvo permB. O vínculo RolePermission permanece ativo,
        // mas a contagem deve desconsiderá-lo porque a Permission referenciada não está mais ativa.
        var del = await _client.DeleteAsync($"/api/v1/permissions/{permB}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var dto = await GetRoleByIdAsync(roleId);
        Assert.Equal(1, dto.PermissionsCount);

        // Cenário paralelo: usuário soft-deletado também não conta no UsersCount.
        var uActive = await CreateUserAsync("cnt.sdt.active@example.com");
        var uDeleted = await CreateUserAsync("cnt.sdt.deleted@example.com");
        await LinkUserRoleAsync(uActive, roleId);
        await LinkUserRoleAsync(uDeleted, roleId);

        var delUser = await _client.DeleteAsync($"/api/v1/users/{uDeleted}");
        Assert.Equal(HttpStatusCode.NoContent, delUser.StatusCode);

        var dto2 = await GetRoleByIdAsync(roleId);
        Assert.Equal(1, dto2.UsersCount);
    }

    [Fact]
    public async Task GetAll_IncludeDeleted_SoftDeletedRole_StillReportsCountsFromActiveLinks()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_INC");
        var roleId = await CreateRoleAsync(systemId, "ROLE_CNT_DELETED");

        var routeId = await CreateRouteAsync(systemId, "RT_CNT_INC_1");
        var typeId = await CreatePermissionTypeAsync("PT_CNT_INC_1");
        var permA = await CreatePermissionAsync(routeId, typeId);
        await LinkRolePermissionAsync(roleId, permA);

        var u = await CreateUserAsync("cnt.inc.user@example.com");
        await LinkUserRoleAsync(u, roleId);

        // Soft-delete da role. Os vínculos ativos permanecem; as contagens devem refleti-los.
        var delRole = await _client.DeleteAsync($"/api/v1/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, delRole.StatusCode);

        var dto = await GetRoleFromListByIdAsync(systemId, roleId, includeDeleted: true);
        Assert.NotNull(dto.DeletedAt);
        Assert.Equal(1, dto.PermissionsCount);
        Assert.Equal(1, dto.UsersCount);
    }

    /// <summary>
    /// Valida que o número de comandos SQL executados pelo <c>GET /roles</c> permanece constante
    /// quando o número de roles na página cresce. Se a implementação fosse N+1, a quantidade de
    /// comandos cresceria proporcionalmente; com subselects projetados, fica fixa em 2 (count + select).
    /// </summary>
    [Fact]
    public async Task GetAll_NoNPlusOne_QueryCountIsConstantRegardlessOfRoleCount()
    {
        var systemId = await CreateSystemAsync("ROLE_CNT_NN");

        var routeId = await CreateRouteAsync(systemId, "RT_CNT_NN_1");
        var typeId = await CreatePermissionTypeAsync("PT_CNT_NN_1");
        var permId = await CreatePermissionAsync(routeId, typeId);

        // Cria 1 role com vínculos para baseline.
        var firstRoleId = await CreateRoleAsync(systemId, "ROLE_CNT_NN_001");
        await LinkRolePermissionAsync(firstRoleId, permId);
        var firstUserId = await CreateUserAsync("cnt.nn.first@example.com");
        await LinkUserRoleAsync(firstUserId, firstRoleId);

        var commandsForOne = await CountCommandsAsync($"/api/v1/roles?systemId={systemId}&pageSize=100");

        // Cria mais 4 roles, cada uma com 1 vínculo de permission e 1 vínculo de user.
        for (var i = 2; i <= 5; i++)
        {
            var roleId = await CreateRoleAsync(systemId, $"ROLE_CNT_NN_{i:D3}");
            await LinkRolePermissionAsync(roleId, permId);
            var userId = await CreateUserAsync($"cnt.nn.{i}@example.com");
            await LinkUserRoleAsync(userId, roleId);
        }

        var commandsForFive = await CountCommandsAsync($"/api/v1/roles?systemId={systemId}&pageSize=100");

        Assert.Equal(commandsForOne, commandsForFive);
    }

    /// <summary>
    /// Conta quantos comandos SQL foram disparados durante a requisição. Usa um interceptor
    /// registrado no <see cref="AppDbContext"/> via <see cref="DbContextOptions"/> — a contagem
    /// começa do zero a cada chamada porque o interceptor é resetado e a request inteira passa por ele.
    /// </summary>
    private async Task<int> CountCommandsAsync(string url)
    {
        _factory.Interceptor.Reset();
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return _factory.Interceptor.Count;
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
        int PermissionsCount,
        int UsersCount,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    private sealed record RefDto(Guid Id, string? Code, string? Name);

    /// <summary>
    /// Factory de teste especializada que injeta o <see cref="QueryCountInterceptor"/> no
    /// <see cref="AppDbContext"/>. Preserva o comportamento do <see cref="WebAppFactory"/> base
    /// (banco PostgreSQL dedicado, seeders) e apenas adiciona o interceptor para contagem de comandos.
    /// </summary>
    private sealed class CountingWebAppFactory : WebAppFactory
    {
        public QueryCountInterceptor Interceptor { get; } = new();

        protected override IReadOnlyList<IInterceptor> AdditionalDbInterceptors =>
            new IInterceptor[] { Interceptor };
    }

    /// <summary>
    /// Conta cada comando SQL executado pelo EF Core enquanto o interceptor está atrelado a um
    /// <see cref="AppDbContext"/>. O contador é por instância (não estático) para evitar interferência
    /// entre fixtures que rodam em paralelo no mesmo processo de teste.
    /// </summary>
    private sealed class QueryCountInterceptor : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Volatile.Write(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
