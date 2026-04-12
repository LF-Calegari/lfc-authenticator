using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class DefaultSystemUserSeederTests : IAsyncLifetime
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
    public async Task RootUser_StoredPassword_IsNotPlaintext()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = DefaultSystemUserSeeder.RootEmail.Trim().ToLowerInvariant();
        var row = await db.Users.AsNoTracking().IgnoreQueryFilters().FirstAsync(u => u.Email == normalized);
        var credential = DefaultSystemUserSeeder.ResolveCredential();
        Assert.NotEqual(credential, row.Password);
        Assert.True(row.Password.Length > 80, "Esperado hash PBKDF2 na coluna Password, não texto plano.");
    }

    [Fact]
    public async Task SystemUsers_ExistAfterBootstrap_CanLoginWithSeededCredentials()
    {
        var cases = new[]
        {
            new { Email = DefaultSystemUserSeeder.RootEmail, Password = DefaultSystemUserSeeder.ResolveCredential() },
            new { Email = DefaultSystemUserSeeder.AdminEmail, Password = ResolveFromEnv("ADMIN_SYSTEM_USER_PASSWORD") },
            new { Email = DefaultSystemUserSeeder.DefaultEmail, Password = ResolveFromEnv("DEFAULT_USER_PASSWORD") }
        };

        foreach (var c in cases)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = c.Email, password = c.Password },
                TestApiClient.JsonOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<LoginTokenDto>(TestApiClient.JsonOptions);
            Assert.NotNull(body);
            Assert.False(string.IsNullOrWhiteSpace(body.Token));
        }
    }

    [Fact]
    public async Task EnsureDefaultUserAsync_Idempotent_DoesNotDuplicateSystemUsers()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DefaultSystemUserSeeder.EnsureDefaultUserAsync(db);
            await DefaultSystemUserSeeder.EnsureDefaultUserAsync(db);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emails = new[]
            {
                DefaultSystemUserSeeder.RootEmail.Trim().ToLowerInvariant(),
                DefaultSystemUserSeeder.AdminEmail.Trim().ToLowerInvariant(),
                DefaultSystemUserSeeder.DefaultEmail.Trim().ToLowerInvariant()
            };

            foreach (var email in emails)
            {
                var count = await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == email);
                Assert.Equal(1, count);
            }
        }
    }

    [Fact]
    public void ResolveCredential_WhenEnvVarIsSet_ReturnsValue()
    {
        var credential = DefaultSystemUserSeeder.ResolveCredential();
        Assert.False(string.IsNullOrWhiteSpace(credential));
    }

    [Fact]
    public void ResolveCredential_WhenEnvVarIsMissing_ThrowsInvalidOperationException()
    {
        var original = Environment.GetEnvironmentVariable("DEFAULT_SYSTEM_USER_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("DEFAULT_SYSTEM_USER_PASSWORD", null);
            var ex = Assert.Throws<InvalidOperationException>(() => DefaultSystemUserSeeder.ResolveCredential());
            Assert.Contains("DEFAULT_SYSTEM_USER_PASSWORD", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEFAULT_SYSTEM_USER_PASSWORD", original);
        }
    }

    private sealed class LoginTokenDto
    {
        public string Token { get; set; } = string.Empty;
    }

    private static string ResolveFromEnv(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }
}
