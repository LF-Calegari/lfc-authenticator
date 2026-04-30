using System.Net;
using System.Text.Json;
using Xunit;

namespace AuthService.Tests;

/// <summary>Valida prefixo global /api/v1 e documentação Swagger em /docs (issue #36).</summary>
public class ApiVersioningAndDocsTests : IAsyncLifetime
{
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = _factory.CreateApiClient();
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Health_WithoutVersionPrefix_IsNotPublicApi()
    {
        // FallbackPolicy exige JWT; rota sem /api/v1 não existe como endpoint versionado → desafio 401.
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithApiV1Prefix_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/health");
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Users_WithoutVersionPrefix_IsNotPublicApi()
    {
        var response = await _client.GetAsync("/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerJson_ContainsOnlyOfficialContractPaths()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain("/api/v1/roles-permissions", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/v1", p, StringComparison.OrdinalIgnoreCase));

        var expected = new[]
        {
            "/api/v1/auth/login",
            "/api/v1/auth/logout",
            "/api/v1/auth/permissions",
            "/api/v1/auth/verify-token",
            "/api/v1/clients",
            "/api/v1/clients/{id}",
            "/api/v1/clients/{id}/emails",
            "/api/v1/clients/{id}/emails/{emailId}",
            "/api/v1/clients/{id}/mobiles",
            "/api/v1/clients/{id}/mobiles/{phoneId}",
            "/api/v1/clients/{id}/phones",
            "/api/v1/clients/{id}/phones/{phoneId}",
            "/api/v1/clients/{id}/restore",
            "/api/v1/health",
            "/api/v1/permissions",
            "/api/v1/permissions/types",
            "/api/v1/permissions/types/{id}",
            "/api/v1/permissions/types/{id}/restore",
            "/api/v1/permissions/{id}",
            "/api/v1/permissions/{id}/restore",
            "/api/v1/roles",
            "/api/v1/roles/{id}",
            "/api/v1/roles/{id}/restore",
            "/api/v1/roles/{roleId}/permissions",
            "/api/v1/roles/{roleId}/permissions/{permissionId}",
            "/api/v1/systems",
            "/api/v1/systems/routes",
            "/api/v1/systems/routes/sync",
            "/api/v1/systems/routes/{id}",
            "/api/v1/systems/routes/{id}/restore",
            "/api/v1/systems/{id}",
            "/api/v1/systems/{id}/restore",
            "/api/v1/tokens/types",
            "/api/v1/tokens/types/{id}",
            "/api/v1/tokens/types/{id}/restore",
            "/api/v1/users",
            "/api/v1/users/{id}",
            "/api/v1/users/{id}/password",
            "/api/v1/users/{id}/restore",
            "/api/v1/users/{userId}/permissions",
            "/api/v1/users/{userId}/permissions/{permissionId}",
            "/api/v1/users/{userId}/roles",
            "/api/v1/users/{userId}/roles/{roleId}"
        };

        Assert.Equal(expected.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    [Fact]
    public async Task Docs_ReturnsSwaggerUi()
    {
        var response = await _client.GetAsync("/docs/index.html");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger", html, StringComparison.OrdinalIgnoreCase);
    }
}
