using System.Net;
using Xunit;

namespace AuthService.Tests;

/// <summary>Valida prefixo global /v1 e documentação Swagger em /docs (issue #25).</summary>
public class ApiVersioningAndDocsTests : IAsyncLifetime
{
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = _factory.CreateClient();
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
        // FallbackPolicy exige JWT; rota sem /v1 não existe como endpoint versionado → desafio 401.
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithV1Prefix_ReturnsOk()
    {
        var response = await _client.GetAsync("/v1/health");
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
    public async Task SwaggerJson_ContainsVersionedPaths()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"/v1/", json, StringComparison.Ordinal);
        Assert.Contains("/v1/users", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Docs_ReturnsSwaggerUi()
    {
        var response = await _client.GetAsync("/docs");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger", html, StringComparison.OrdinalIgnoreCase);
    }
}
