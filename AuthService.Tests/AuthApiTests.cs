using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class AuthApiTests : IAsyncLifetime
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

    private static object LoginBody(string email, string password) => new { email, password };

    private sealed class LoginDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }

    private sealed class VerifyDto
    {
        public AuthUserDto? User { get; set; }
        public List<Guid>? PermissionIds { get; set; }
    }

    private sealed class AuthUserDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Identity { get; set; }
        public bool Active { get; set; }
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "Auth User", email = "auth.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            JsonOptions);

        var response = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("auth.user@example.com", "SenhaSegura1!"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LoginDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrWhiteSpace(dto.Token));
        Assert.True(dto.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_NormalizesEmailCase()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "Case User", email = "case.auth@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            JsonOptions);

        var response = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("  CASE.AUTH@EXAMPLE.COM  ", "SenhaSegura1!"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "U2", email = "u2@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            JsonOptions);

        var response = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("u2@example.com", "outraSenha"), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "Inativo", email = "inativo@example.com", password = "SenhaSegura1!", identity = 1, active = false },
            JsonOptions);

        var response = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("inativo@example.com", "SenhaSegura1!"), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidPayload_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = "", password = "" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithoutHeader_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/auth/verify-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_ValidToken_ReturnsUserAndPermissions()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "V User", email = "v.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            JsonOptions);

        var login = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("v.user@example.com", "SenhaSegura1!"), JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginDto>(JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(JsonOptions);
        Assert.NotNull(body?.User);
        Assert.Equal("v.user@example.com", body.User.Email);
        Assert.NotNull(body.PermissionIds);
    }

    [Fact]
    public async Task Logout_ThenVerifyToken_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/users",
            new { name = "L User", email = "l.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            JsonOptions);

        var login = await _client.PostAsJsonAsync("/auth/login",
            LoginBody("l.user@example.com", "SenhaSegura1!"), JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginDto>(JsonOptions);
        Assert.NotNull(loginDto);
        var token = loginDto.Token;

        using (var logoutReq = new HttpRequestMessage(HttpMethod.Get, "/auth/logout"))
        {
            logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var logoutRes = await _client.SendAsync(logoutReq);
            Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);
        }

        using (var verifyReq = new HttpRequestMessage(HttpMethod.Get, "/auth/verify-token"))
        {
            verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var verifyRes = await _client.SendAsync(verifyReq);
            Assert.Equal(HttpStatusCode.Unauthorized, verifyRes.StatusCode);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "l.user@example.com");
            Assert.True(row.TokenVersion >= 1);
        }
    }

    [Fact]
    public async Task VerifyToken_InvalidBearerToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
