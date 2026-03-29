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
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DefaultUser_StoredPassword_IsNotPlaintext()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = DefaultSystemUserSeeder.Email.Trim().ToLowerInvariant();
        var row = await db.Users.AsNoTracking().IgnoreQueryFilters().FirstAsync(u => u.Email == normalized);
        Assert.NotEqual(DefaultSystemUserSeeder.Password, row.Password);
        Assert.True(row.Password.Length > 80, "Esperado hash PBKDF2 na coluna Password, não texto plano.");
    }

    [Fact]
    public async Task DefaultUser_ExistsAfterBootstrap_CanLoginWithSeededCredentials()
    {
        var response = await _client.PostAsJsonAsync("/v1/auth/login",
            new { email = DefaultSystemUserSeeder.Email, password = DefaultSystemUserSeeder.Password },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginTokenDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
    }

    [Fact]
    public async Task EnsureDefaultUserAsync_Idempotent_DoesNotDuplicateUser()
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
            var normalized = DefaultSystemUserSeeder.Email.Trim().ToLowerInvariant();
            var count = await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == normalized);
            Assert.Equal(1, count);
        }
    }

    private sealed class LoginTokenDto
    {
        public string Token { get; set; } = string.Empty;
    }
}
