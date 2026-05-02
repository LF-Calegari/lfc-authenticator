using AuthService.Auth;
using Microsoft.AspNetCore.Authentication;

namespace AuthService.OpenApi;

/// <summary>
/// Middleware que protege a UI do Swagger servida pelo Swashbuckle em <c>/docs</c>. A UI não é
/// mapeada como endpoint MVC, então a <see cref="Microsoft.AspNetCore.Authorization.AuthorizationOptions.FallbackPolicy"/>
/// não é aplicada automaticamente. Este middleware autentica explicitamente o request com o esquema
/// Bearer da aplicação e recusa com 401 quando não há principal autenticado, alinhando o acesso
/// à documentação ao mesmo controle de acesso da API (issue #95). Endpoints expostos sob
/// <c>/swagger/{documentName}/swagger.json</c> são endpoints de roteamento e já são cobertos pela
/// fallback policy de autorização configurada em <c>Program.cs</c>.
/// </summary>
public sealed class SwaggerAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SwaggerAuthorizationMiddleware> _logger;

    public SwaggerAuthorizationMiddleware(RequestDelegate next, ILogger<SwaggerAuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsSwaggerUiPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var authResult = await context.AuthenticateAsync(BearerAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Principal?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning(
                "Acesso anônimo negado a endpoint de documentação {Path} (origem {RemoteIp}).",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = $"{BearerAuthenticationDefaults.AuthenticationScheme} realm=\"docs\"";
            return;
        }

        context.User = authResult.Principal;
        await _next(context);
    }

    /// <summary>
    /// Indica se a request alvo deve ser tratada como acesso à UI do Swagger servida em
    /// <c>/docs</c> e seus recursos estáticos. O documento OpenAPI sob <c>/swagger</c> é um
    /// endpoint de roteamento e fica fora do escopo deste middleware (já protegido pela
    /// fallback policy do <see cref="Microsoft.AspNetCore.Authorization.AuthorizationMiddleware"/>).
    /// </summary>
    public static bool IsSwaggerUiPath(PathString path)
    {
        if (!path.HasValue)
            return false;

        return path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase);
    }
}
