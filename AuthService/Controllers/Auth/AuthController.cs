using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.Auth;

[ApiController]
[Route("auth")]
public partial class AuthController : ControllerBase
{
    private const string InvalidCredentialsMessage = "Credenciais inválidas.";
    private const string InvalidTokenMessage = "Token inválido.";
    private const string UserNotFoundMessage = "Usuário não encontrado.";

    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, IJwtTokenService jwt, ILogger<AuthController> logger)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
    }

    public class LoginRequest
    {
        [Required(ErrorMessage = "Email é obrigatório.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        [MaxLength(320)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password é obrigatório.")]
        [MaxLength(60)]
        public string Password { get; set; } = string.Empty;
    }

    public record LoginResponse(string Token);

    public record VerifyTokenResponse(
        Guid Id,
        string Name,
        string Email,
        int Identity,
        IReadOnlyList<Guid> Permissions,
        IReadOnlyList<string> PermissionCodes,
        IReadOnlyList<string> RouteCodes);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var email = request.Email.Trim().ToLowerInvariant();
        var password = request.Password.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Unauthorized(new { message = InvalidCredentialsMessage });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            _logger.LogWarning("Tentativa de login falhou para o email {Email}.", email);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var (passwordOk, newStoredPassword) = UserPasswordHasher.Verify(user, password);
        if (!passwordOk)
        {
            _logger.LogWarning("Tentativa de login falhou para o email {Email}.", email);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (newStoredPassword is not null)
        {
            user.Password = newStoredPassword;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        if (!user.Active)
        {
            _logger.LogWarning("Tentativa de login para usuário inativo {UserId}.", user.Id);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var token = _jwt.CreateAccessToken(user.Id, user.TokenVersion, out _);
        return Ok(new LoginResponse(token));
    }

    [HttpGet("verify-token")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> VerifyToken()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = InvalidTokenMessage });

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Unauthorized(new { message = UserNotFoundMessage });

        var permissionIds = (await EffectivePermissionIds.GetForUserAsync(_db, userId))
            .OrderBy(x => x)
            .ToList();
        var permissionCodes = await ResolvePermissionCodesAsync(permissionIds, HttpContext.RequestAborted);
        var routeCodes = await ResolveRouteCodesAsync(permissionIds, HttpContext.RequestAborted);
        return Ok(new VerifyTokenResponse(
            user.Id,
            user.Name,
            user.Email,
            user.Identity,
            permissionIds,
            permissionCodes,
            routeCodes)
        );
    }

    [HttpGet("logout")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logout()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = InvalidTokenMessage });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Unauthorized(new { message = UserNotFoundMessage });

        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogLogoutCompleted(userId);
        return Ok(new { message = "Sessão encerrada." });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logout concluído para o usuário {UserId}.")]
    private partial void LogLogoutCompleted(Guid userId);

    private async Task<IReadOnlyList<string>> ResolvePermissionCodesAsync(
        List<Guid> permissionIds,
        CancellationToken cancellationToken)
    {
        if (permissionIds.Count == 0)
            return Array.Empty<string>();

        var systemTypePairs = await _db.Permissions.AsNoTracking()
            .Where(p => permissionIds.Contains(p.Id))
            .Join(
                _db.Systems.AsNoTracking(),
                p => p.SystemId,
                s => s.Id,
                (p, s) => new { p.PermissionTypeId, SystemCode = s.Code })
            .Join(
                _db.PermissionTypes.AsNoTracking(),
                x => x.PermissionTypeId,
                t => t.Id,
                (x, t) => new { x.SystemCode, TypeCode = t.Code })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (systemTypePairs.Count == 0)
            return Array.Empty<string>();

        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pair in systemTypePairs)
        {
            var resources = PermissionCatalog.GetResourcesForSystem(pair.SystemCode);
            if (resources.Count == 0)
                continue;

            foreach (var resource in resources)
                codes.Add(PermissionCodeFormatter.Format(resource, pair.TypeCode));
        }

        return codes.ToArray();
    }

    private async Task<IReadOnlyList<string>> ResolveRouteCodesAsync(
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken)
    {
        if (permissionIds.Count == 0)
            return Array.Empty<string>();

        var kurttoPermissionTypes = await _db.Permissions.AsNoTracking()
            .Where(p => permissionIds.Contains(p.Id))
            .Join(
                _db.Systems.AsNoTracking(),
                p => p.SystemId,
                s => s.Id,
                (p, s) => new { p.PermissionTypeId, SystemCode = s.Code })
            .Where(x => x.SystemCode == "kurtto")
            .Join(
                _db.PermissionTypes.AsNoTracking(),
                x => x.PermissionTypeId,
                t => t.Id,
                (_, t) => t.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allowedPolicies = kurttoPermissionTypes
            .Select(MapKurttoPermissionTypeToPolicy)
            .Where(policy => policy is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        if (allowedPolicies.Count == 0)
            return Array.Empty<string>();

        return KurttoAccessSeeder.KurttoAdminRoutes
            .Where(route => allowedPolicies.Contains(route.TargetPermissionPolicy))
            .Select(route => route.Code)
            .OrderBy(code => code)
            .ToArray();
    }

    private static string? MapKurttoPermissionTypeToPolicy(string permissionTypeCode) =>
        permissionTypeCode switch
        {
            "create" => PermissionPolicies.KurttoCreate,
            "read" => PermissionPolicies.KurttoRead,
            "update" => PermissionPolicies.KurttoUpdate,
            "delete" => PermissionPolicies.KurttoDelete,
            "restore" => PermissionPolicies.KurttoRestore,
            _ => null
        };
}
