using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class IntegrationBootstrapSeederTests : IAsyncLifetime
{
    private WebAppFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _client = _factory.CreateApiClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BootstrapUser_StoredPassword_IsNotPlaintext()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = IntegrationBootstrapSeeder.Email.Trim().ToLowerInvariant();
        var row = await db.Users.AsNoTracking().IgnoreQueryFilters().FirstAsync(u => u.Email == normalized);
        var credential = IntegrationBootstrapSeeder.ResolveCredential();
        Assert.NotEqual(credential, row.Password);
        Assert.True(row.Password.Length > 80, "Esperado hash PBKDF2 na coluna Password, não texto plano.");
    }

    [Fact]
    public async Task BootstrapUser_CanLoginWithExternalizedCredential()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = IntegrationBootstrapSeeder.Email, password = IntegrationBootstrapSeeder.ResolveCredential() },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginTokenDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
    }

    [Fact]
    public async Task EnsureBootstrapUserAsync_Idempotent_DoesNotDuplicateUser()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await IntegrationBootstrapSeeder.EnsureBootstrapUserAsync(db);
            await IntegrationBootstrapSeeder.EnsureBootstrapUserAsync(db);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var normalized = IntegrationBootstrapSeeder.Email.Trim().ToLowerInvariant();
            var count = await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == normalized);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public void ResolveCredential_WhenEnvVarIsSet_ReturnsValue()
    {
        var credential = IntegrationBootstrapSeeder.ResolveCredential();
        Assert.False(string.IsNullOrWhiteSpace(credential));
    }

    [Fact]
    public void ResolveCredential_WhenEnvVarIsMissing_ThrowsInvalidOperationException()
    {
        var original = Environment.GetEnvironmentVariable("INTEGRATION_BOOTSTRAP_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("INTEGRATION_BOOTSTRAP_PASSWORD", null);
            var ex = Assert.Throws<InvalidOperationException>(() => IntegrationBootstrapSeeder.ResolveCredential());
            Assert.Contains("INTEGRATION_BOOTSTRAP_PASSWORD", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTEGRATION_BOOTSTRAP_PASSWORD", original);
        }
    }

    private sealed class LoginTokenDto
    {
        public string Token { get; set; } = string.Empty;
    }
}
