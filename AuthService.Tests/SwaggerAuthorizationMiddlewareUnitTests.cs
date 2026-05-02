using System.Security.Claims;
using AuthService.Auth;
using AuthService.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuthService.Tests;

/// <summary>
/// Testes unitários do <see cref="SwaggerAuthorizationMiddleware"/>. Cobrem os ramos do middleware
/// de forma direta (sem subir o app via <see cref="WebAppFactory"/>) para garantir cobertura ≥80%
/// no SonarCloud (issue #95 / PR #178).
/// </summary>
public class SwaggerAuthorizationMiddlewareUnitTests
{
    [Theory]
    [InlineData("/docs", true)]
    [InlineData("/docs/", true)]
    [InlineData("/docs/index.html", true)]
    [InlineData("/DOCS/index.html", true)]
    [InlineData("/swagger/v1/swagger.json", false)]
    [InlineData("/api/v1/health", false)]
    [InlineData("/", false)]
    public void IsSwaggerUiPath_DiscriminatesDocsFromOtherPaths(string path, bool expected)
    {
        var result = SwaggerAuthorizationMiddleware.IsSwaggerUiPath(new PathString(path));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsSwaggerUiPath_EmptyPath_ReturnsFalse()
    {
        var result = SwaggerAuthorizationMiddleware.IsSwaggerUiPath(PathString.Empty);
        Assert.False(result);
    }

    [Fact]
    public async Task InvokeAsync_NonSwaggerPath_BypassesAuthAndCallsNext()
    {
        var nextCalled = false;
        var middleware = new SwaggerAuthorizationMiddleware(
            ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SwaggerAuthorizationMiddleware>.Instance);

        var context = CreateHttpContext("/api/v1/users");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled, "Para path fora de /docs o middleware deve chamar _next sem alterar a response.");
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("WWW-Authenticate"));
    }

    [Fact]
    public async Task InvokeAsync_DocsPath_WithoutAuth_Returns401AndDoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = new SwaggerAuthorizationMiddleware(
            ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SwaggerAuthorizationMiddleware>.Instance);

        var context = CreateHttpContext("/docs/index.html");
        ConfigureAuthService(context, AuthenticateResult.NoResult());

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled, "Sem auth, _next não deve ser invocado.");
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.True(context.Response.Headers.TryGetValue("WWW-Authenticate", out var wwwAuth));
        Assert.Contains("Bearer", wwwAuth.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_DocsPath_FailedAuth_Returns401AndDoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = new SwaggerAuthorizationMiddleware(
            ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SwaggerAuthorizationMiddleware>.Instance);

        var context = CreateHttpContext("/docs/index.html");
        ConfigureAuthService(context, AuthenticateResult.Fail("token inválido"));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DocsPath_AuthSucceeds_CallsNextAndAttachesUser()
    {
        var nextCalled = false;
        ClaimsPrincipal? userSeenByNext = null;

        var middleware = new SwaggerAuthorizationMiddleware(
            ctx =>
            {
                nextCalled = true;
                userSeenByNext = ctx.User;
                return Task.CompletedTask;
            },
            NullLogger<SwaggerAuthorizationMiddleware>.Instance);

        var context = CreateHttpContext("/docs/index.html");
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")) },
            BearerAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        ConfigureAuthService(
            context,
            AuthenticateResult.Success(new AuthenticationTicket(principal, BearerAuthenticationDefaults.AuthenticationScheme)));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotNull(userSeenByNext);
        Assert.True(userSeenByNext!.Identity?.IsAuthenticated);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void ConfigureAuthService(HttpContext context, AuthenticateResult result)
    {
        var stub = new StubAuthenticationService(result);
        var services = new ServiceCollectionStub(stub);
        context.RequestServices = services;
        context.Features.Set<IServiceProvidersFeature>(new ServiceProvidersFeatureStub(services));
    }

    private sealed class StubAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        private readonly AuthenticateResult _result = result;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(_result);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    private sealed class ServiceCollectionStub(IAuthenticationService authService) : IServiceProvider
    {
        private readonly IAuthenticationService _authService = authService;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAuthenticationService))
                return _authService;
            return null;
        }
    }

    private sealed class ServiceProvidersFeatureStub(IServiceProvider provider) : IServiceProvidersFeature
    {
        public IServiceProvider RequestServices { get; set; } = provider;
    }
}
