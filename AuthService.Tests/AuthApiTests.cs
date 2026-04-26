using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthService.Controllers.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

public class AuthApiTests : IAsyncLifetime
{
    private const string TestJwtSecret = "integration-tests-jwt-secret-key-32chars!!";
    private static readonly string[] ExpectedKurttoReadOnlyCodes = new[] { "perm:Kurtto.Read" };
    private WebAppFactory _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _anon = null!;
    private Guid _kurttoSystemId;
    private Guid _authenticatorSystemId;

    public async Task InitializeAsync()
    {
        _factory = new WebAppFactory();
        _admin = await TestApiClient.CreateAuthenticatedAsync(_factory);
        _anon = _factory.CreateApiClient();
        _kurttoSystemId = await TestApiClient.GetSystemIdAsync(_factory, "kurtto");
        _authenticatorSystemId = await TestApiClient.GetSystemIdAsync(_factory, "authenticator");
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _anon.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private object LoginBody(string email, string password) => new { email, password, systemId = _kurttoSystemId };

    private static object LoginBodyForSystem(string email, string password, Guid systemId) =>
        new { email, password, systemId };

    private sealed class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
    }

    private sealed class VerifyDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Identity { get; set; }
        public List<Guid>? Permissions { get; set; }
        public List<string>? PermissionCodes { get; set; }
        public List<string>? RouteCodes { get; set; }
    }

    [Fact]
    public async Task Login_AfterUserCreate_StoredPassword_IsHashedNotPlaintext()
    {
        const string plain = "SenhaSegura1!";
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Hash Check", email = "hash.check@example.com", password = plain, identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("hash.check@example.com", plain), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "hash.check@example.com");
        Assert.NotEqual(plain, row.Password);
        Assert.True(row.Password.Length > 80, "Esperado hash PBKDF2 na coluna Password.");
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Auth User", email = "auth.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("auth.user@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrWhiteSpace(dto.Token));
    }

    [Fact]
    public async Task Login_NormalizesEmailCase()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Case User", email = "case.auth@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("  CASE.AUTH@EXAMPLE.COM  ", "SenhaSegura1!"), TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "U2", email = "u2@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("u2@example.com", "outraSenha"), TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_ReturnsUnauthorized()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Inativo", email = "inativo@example.com", password = "SenhaSegura1!", identity = 1, active = false },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("inativo@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidPayload_ReturnsBadRequest()
    {
        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" }, TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithoutBearer_ReturnsUnauthorized()
    {
        // Sem Authorization e sem X-System-Id: o pipeline de autenticação roda primeiro e devolve 401.
        var response = await _anon.GetAsync("/api/v1/auth/verify-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_ValidToken_ReturnsUserAndPermissions()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "V User", email = "v.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("v.user@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("v.user@example.com", body.Email);
        Assert.NotNull(body.Permissions);
        Assert.NotNull(body.PermissionCodes);
        Assert.NotNull(body.RouteCodes);
    }

    [Fact]
    public async Task VerifyToken_RootUser_ReturnsKurttoRouteCodes()
    {
        var response = await _admin.GetAsync("/api/v1/auth/verify-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.RouteCodes);

        var expected = new[]
        {
            "KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED",
            "KURTTO_V1_URLS_LIST_INCLUDE_DELETED",
            "KURTTO_V1_URLS_PATCH_RESTORE"
        };

        foreach (var code in expected)
            Assert.Contains(code, body.RouteCodes);
    }

    [Fact]
    public async Task VerifyToken_RootUser_ReturnsAllPermissionPolicyCodes()
    {
        var response = await _admin.GetAsync("/api/v1/auth/verify-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.PermissionCodes);

        var expected = new[]
        {
            // authenticator system
            "perm:Clients.Create", "perm:Clients.Read", "perm:Clients.Update", "perm:Clients.Delete", "perm:Clients.Restore",
            "perm:Users.Create", "perm:Users.Read", "perm:Users.Update", "perm:Users.Delete", "perm:Users.Restore",
            "perm:Systems.Create", "perm:Systems.Read", "perm:Systems.Update", "perm:Systems.Delete", "perm:Systems.Restore",
            "perm:SystemsRoutes.Create", "perm:SystemsRoutes.Read", "perm:SystemsRoutes.Update", "perm:SystemsRoutes.Delete", "perm:SystemsRoutes.Restore",
            "perm:SystemTokensTypes.Create", "perm:SystemTokensTypes.Read", "perm:SystemTokensTypes.Update", "perm:SystemTokensTypes.Delete", "perm:SystemTokensTypes.Restore",
            "perm:Permissions.Create", "perm:Permissions.Read", "perm:Permissions.Update", "perm:Permissions.Delete", "perm:Permissions.Restore",
            "perm:PermissionsTypes.Create", "perm:PermissionsTypes.Read", "perm:PermissionsTypes.Update", "perm:PermissionsTypes.Delete", "perm:PermissionsTypes.Restore",
            "perm:Roles.Create", "perm:Roles.Read", "perm:Roles.Update", "perm:Roles.Delete", "perm:Roles.Restore",
            // kurtto system
            "perm:Kurtto.Create", "perm:Kurtto.Read", "perm:Kurtto.Update", "perm:Kurtto.Delete", "perm:Kurtto.Restore"
        };

        foreach (var code in expected)
            Assert.Contains(code, body.PermissionCodes);

        // Garante que todo code segue o formato perm:<Recurso>.<Acao>
        foreach (var code in body.PermissionCodes)
            Assert.StartsWith("perm:", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyToken_UserWithoutPermissions_ReturnsEmptyPermissionCodes()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "No Perms", email = "no.perms@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("no.perms@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.PermissionCodes);
        Assert.Empty(body.PermissionCodes);
        // Permissions e RouteCodes mantem comportamento atual (presentes no payload).
        Assert.NotNull(body.Permissions);
        Assert.NotNull(body.RouteCodes);
    }

    [Fact]
    public async Task VerifyToken_UserWithOnlyKurttoRead_ReturnsKurttoReadCodeOnly()
    {
        // Cria usuario sem role
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Kurtto Reader", email = "kurtto.reader@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        // Concede apenas a permissao kurtto+read direto ao usuario
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "kurtto.reader@example.com");
            var kurttoReadId = await db.Permissions.AsNoTracking()
                .Where(p => p.SystemId == db.Systems.Where(s => s.Code == "kurtto").Select(s => s.Id).First()
                    && p.PermissionTypeId == db.PermissionTypes.Where(t => t.Code == "read").Select(t => t.Id).First())
                .Select(p => p.Id)
                .FirstAsync();

            db.UserPermissions.Add(new AuthService.Models.AppUserPermission
            {
                UserId = user.Id,
                PermissionId = kurttoReadId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeletedAt = null
            });
            await db.SaveChangesAsync();
        }

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("kurtto.reader@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.PermissionCodes);
        Assert.Equal(ExpectedKurttoReadOnlyCodes, body.PermissionCodes);
        // Nao expoe codes de outros recursos (Users.Read etc.) — kurtto e isolado.
        Assert.DoesNotContain("perm:Users.Read", body.PermissionCodes);
    }

    [Fact]
    public async Task Logout_ThenVerifyToken_ReturnsUnauthorized()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "L User", email = "l.user@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("l.user@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);
        var token = loginDto.Token;

        using (var logoutReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/logout"))
        {
            logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var logoutRes = await _anon.SendAsync(logoutReq);
            Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);
        }

        using (var verifyReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token"))
        {
            verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            verifyReq.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
            var verifyRes = await _anon.SendAsync(verifyReq);
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
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_MalformedAuthorizationHeader_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.TryAddWithoutValidation("Authorization", "Basic abc123");
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_EmptyBearerToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer   ");
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithInvalidJwtConfiguration_ReturnsUnauthorized()
    {
        using var invalidSecretFactory = _factory.WithWebHostBuilder(static builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:Secret"] = "short-secret"
                });
            });
        });
        using var client = invalidSecretFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "abc");
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_ExpiredJwt_ReturnsUnauthorized()
    {
        var token = CreateHs256Token(
            TestJwtSecret,
            new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["tv"] = 0,
                ["sys"] = _kurttoSystemId.ToString("D"),
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()
            });

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WrongSignatureJwt_ReturnsUnauthorized()
    {
        var token = CreateHs256Token(
            "wrong-signature-secret-key-32chars!!",
            new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["tv"] = 0,
                ["sys"] = _kurttoSystemId.ToString("D"),
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
            });

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithoutSubClaim_ReturnsUnauthorized()
    {
        var token = CreateHs256Token(
            TestJwtSecret,
            new Dictionary<string, object>
            {
                ["tv"] = 0,
                ["sys"] = _kurttoSystemId.ToString("D"),
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
            });

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithoutTokenVersionClaim_ReturnsUnauthorized()
    {
        var token = CreateHs256Token(
            TestJwtSecret,
            new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["sys"] = _kurttoSystemId.ToString("D"),
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
            });

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_WithoutToken_ReturnsUnauthorized()
    {
        using var anon = _factory.CreateApiClient();
        var response = await anon.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_WithValidTokenButWithoutUsersRead_ReturnsForbidden()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Sem Users.Read", email = "sem.users.read@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("sem.users.read@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutSystemId_ReturnsBadRequest()
    {
        // Cria usuário válido para garantir que o 400 vem do systemId, não do email/senha.
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "NoSys User", email = "nosys@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "nosys@example.com", password = "SenhaSegura1!" },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithEmptySystemId_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "EmptySys User", email = "emptysys@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "emptysys@example.com", password = "SenhaSegura1!", systemId = Guid.Empty },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownSystemId_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Unknown User", email = "unknown.sys@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "unknown.sys@example.com", password = "SenhaSegura1!", systemId = Guid.NewGuid() },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithDeletedSystemId_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Del Sys User", email = "del.sys@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        // Cria sistema novo e marca como deletado (soft-delete = inativo).
        Guid deletedSystemId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var system = new AppSystem
            {
                Name = "Sistema Inativo",
                Code = "sistema-inativo-test",
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = now
            };
            db.Systems.Add(system);
            await db.SaveChangesAsync();
            deletedSystemId = system.Id;
        }

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "del.sys@example.com", password = "SenhaSegura1!", systemId = deletedSystemId },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidSystemId_ReturnsToken_AndJwtCarriesSysClaim()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Sys OK User", email = "sys.ok@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var response = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "sys.ok@example.com", password = "SenhaSegura1!", systemId = _kurttoSystemId },
            TestApiClient.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(dto);

        var payload = ReadJwtPayload(dto.Token);
        Assert.Equal(_kurttoSystemId.ToString("D"), payload.GetProperty("sys").GetString());
    }

    [Fact]
    public async Task VerifyToken_WithoutXSystemIdHeader_ReturnsBadRequest()
    {
        // Token válido (claim sys preenchida) mas sem header X-System-Id.
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "VTok NoHdr", email = "vtok.nohdr@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("vtok.nohdr@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithMalformedXSystemIdHeader_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "VTok Bad Hdr", email = "vtok.badhdr@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("vtok.badhdr@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, "not-a-guid");
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithUnknownSystemIdHeader_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "VTok Unk", email = "vtok.unk@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("vtok.unk@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, Guid.NewGuid().ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithDeletedSystemIdHeader_ReturnsBadRequest()
    {
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "VTok Del", email = "vtok.del@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBody("vtok.del@example.com", "SenhaSegura1!"), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        // Cria e marca como deletado um sistema cujo Id será usado no header.
        Guid deletedSystemId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var system = new AppSystem
            {
                Name = "Inativo VTok",
                Code = "inativo-vtok",
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = now
            };
            db.Systems.Add(system);
            await db.SaveChangesAsync();
            deletedSystemId = system.Id;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, deletedSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_CrossSystemToken_ReturnsUnauthorized()
    {
        // Login no sistema kurtto e tenta verify com header X-System-Id apontando pro sistema authenticator.
        await _admin.PostAsJsonAsync("/api/v1/users",
            new { name = "Cross Sys User", email = "cross.sys@example.com", password = "SenhaSegura1!", identity = 1, active = true },
            TestApiClient.JsonOptions);

        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBodyForSystem("cross.sys@example.com", "SenhaSegura1!", _kurttoSystemId), TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, _authenticatorSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_AuthenticatorSystem_ReturnsEmptyRouteCodes_NotKurttoCodes()
    {
        // Root user tem todas as permissões (incluindo authenticator e kurtto).
        // Quando faz login em authenticator, verify-token deve devolver routeCodes vazio
        // (authenticator não declara rotas seedadas), em vez das rotas do kurtto.
        var login = await _anon.PostAsJsonAsync("/api/v1/auth/login",
            LoginBodyForSystem(DefaultSystemUserSeeder.RootEmail, DefaultSystemUserSeeder.ResolveCredential(), _authenticatorSystemId),
            TestApiClient.JsonOptions);
        var loginDto = await login.Content.ReadFromJsonAsync<LoginResponseDto>(TestApiClient.JsonOptions);
        Assert.NotNull(loginDto);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginDto.Token);
        req.Headers.Add(AuthController.SystemIdHeader, _authenticatorSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyDto>(TestApiClient.JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(body.RouteCodes);
        Assert.Empty(body.RouteCodes);
        // Codes do kurtto NÃO devem aparecer quando o sistema solicitado é authenticator.
        Assert.DoesNotContain("KURTTO_V1_URLS_LIST_INCLUDE_DELETED", body.RouteCodes);
        Assert.DoesNotContain("KURTTO_V1_URLS_GET_BY_CODE_INCLUDE_DELETED", body.RouteCodes);
        Assert.DoesNotContain("KURTTO_V1_URLS_PATCH_RESTORE", body.RouteCodes);
    }

    [Fact]
    public async Task VerifyToken_TokenWithoutSysClaim_ReturnsUnauthorized()
    {
        // Token forjado sem claim sys deve falhar na autenticação (handler exige sys).
        var token = CreateHs256Token(
            TestJwtSecret,
            new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["tv"] = 0,
                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
            });

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/verify-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(AuthController.SystemIdHeader, _kurttoSystemId.ToString("D"));
        var response = await _anon.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static JsonElement ReadJwtPayload(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var jsonBytes = Base64UrlDecode(parts[1]);
        using var doc = JsonDocument.Parse(jsonBytes);
        return doc.RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        var pad = 4 - (base64.Length % 4);
        if (pad is > 0 and < 4)
            base64 += new string('=', pad);

        return Convert.FromBase64String(base64);
    }

    private static string CreateHs256Token(string secret, IDictionary<string, object> payload)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerSegment}.{payloadSegment}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
