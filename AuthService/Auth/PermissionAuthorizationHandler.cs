using System.Security.Claims;
using AuthService.Data;
using Microsoft.AspNetCore.Authorization;

namespace AuthService.Auth;

public sealed class PermissionAuthorizationHandler(
    AppDbContext db,
    IPermissionResolver resolver,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _db = db;
    private readonly IPermissionResolver _resolver = resolver;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<PermissionAuthorizationHandler> _logger = logger;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return;

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        var requiredId = await _resolver.ResolveToIdAsync(requirement.Key, ct);
        if (requiredId is null)
        {
            _logger.LogError("Permissão oficial não encontrada no catálogo para a chave {Key}.", requirement.Key);
            return;
        }

        var effective = await EffectivePermissionIds.GetForUserAsync(_db, userId, ct);
        if (effective.Contains(requiredId.Value))
            context.Succeed(requirement);
    }
}
