using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
    private const string SystemIdRequiredMessage = "SystemId é obrigatório.";
    private const string SystemIdHeaderRequiredMessage = "Header X-System-Id é obrigatório.";
    private const string InvalidSystemIdMessage = "Sistema inválido ou inativo.";
    private const string CrossSystemTokenMessage = "Token não é válido para este sistema.";
    private const string RouteCodeHeaderRequiredMessage = "Header X-Route-Code é obrigatório.";
    private const string InvalidRouteCodeMessage = "Rota inválida.";
    private const string RouteForbiddenMessage = "Acesso negado para a rota.";

    /// <summary>Header HTTP que identifica o sistema cliente em <c>GET /auth/verify-token</c> e <c>GET /auth/permissions</c>.</summary>
    public const string SystemIdHeader = "X-System-Id";

    /// <summary>Header HTTP que identifica a rota concreta a ser autorizada em <c>GET /auth/verify-token</c>.</summary>
    public const string RouteCodeHeader = "X-Route-Code";

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

        [Required(ErrorMessage = "SystemId é obrigatório.")]
        public Guid? SystemId { get; set; }
    }

    public record LoginResponse(string Token);

    /// <summary>
    /// Resposta enxuta do <c>verify-token</c> após a separação de catálogo (issue #148).
    /// Carrega apenas a confirmação de validade e janela temporal do JWT.
    /// </summary>
    public record VerifyTokenResponse(
        bool Valid,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt);

    public record PermissionsUserDto(
        Guid Id,
        string Name,
        string Email,
        int Identity);

    /// <summary>
    /// Resposta do <c>GET /auth/permissions</c> com o catálogo de rotas do sistema do header.
    /// Substitui a parte de catálogo do antigo <c>verify-token</c>.
    /// </summary>
    public record PermissionsResponse(
        PermissionsUserDto User,
        IReadOnlyList<string> Routes);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (request.SystemId is not { } systemId || systemId == Guid.Empty)
            return BadRequest(new { message = SystemIdRequiredMessage });

        // Query filter global remove sistemas com DeletedAt != null (soft-delete = inativo).
        var systemExists = await _db.Systems.AsNoTracking()
            .AnyAsync(s => s.Id == systemId, HttpContext.RequestAborted);
        if (!systemExists)
        {
            _logger.LogWarning("Tentativa de login com sistema inválido/inativo {SystemId}.", systemId);
            return BadRequest(new { message = InvalidSystemIdMessage });
        }

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

        var token = _jwt.CreateAccessToken(user.Id, user.TokenVersion, systemId, out _);
        return Ok(new LoginResponse(token));
    }

    [HttpGet("verify-token")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> VerifyToken(
        [FromHeader(Name = SystemIdHeader)] string? systemIdHeader,
        [FromHeader(Name = RouteCodeHeader)] string? routeCodeHeader)
    {
        var systemValidation = await ValidateSystemHeaderAsync(systemIdHeader);
        if (systemValidation.ErrorResult is not null)
            return systemValidation.ErrorResult;
        var headerSystemId = systemValidation.SystemId;

        if (string.IsNullOrWhiteSpace(routeCodeHeader))
            return BadRequest(new { message = RouteCodeHeaderRequiredMessage });

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = InvalidTokenMessage });

        var sysClaim = User.FindFirstValue(JwtBearerAuthenticationHandler.SystemIdClaimType);
        if (string.IsNullOrEmpty(sysClaim) || !Guid.TryParse(sysClaim, out var tokenSystemId))
            return Unauthorized(new { message = InvalidTokenMessage });

        if (tokenSystemId != headerSystemId)
        {
            _logger.LogWarning(
                "Token cruzado detectado: claim sys={TokenSystemId} difere do header X-System-Id={HeaderSystemId}.",
                tokenSystemId,
                headerSystemId);
            return Unauthorized(new { message = CrossSystemTokenMessage });
        }

        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, HttpContext.RequestAborted);
        if (!userExists)
            return Unauthorized(new { message = UserNotFoundMessage });

        // Rota deve existir no catálogo do sistema do header.
        var routeKnown = await _db.Routes.AsNoTracking()
            .AnyAsync(r => r.SystemId == headerSystemId && r.Code == routeCodeHeader, HttpContext.RequestAborted);
        if (!routeKnown)
            return BadRequest(new { message = InvalidRouteCodeMessage });

        var routes = await ResolveRoutesAsync(headerSystemId, HttpContext.RequestAborted);

        if (!routes.Contains(routeCodeHeader, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Acesso à rota {RouteCode} negado para o usuário {UserId} no sistema {SystemId}.",
                routeCodeHeader,
                userId,
                headerSystemId);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = RouteForbiddenMessage });
        }

        if (!TryReadUnixSecondsClaim(JwtBearerAuthenticationHandler.IssuedAtClaimType, out var issuedAt))
            return Unauthorized(new { message = InvalidTokenMessage });

        if (!TryReadUnixSecondsClaim(JwtBearerAuthenticationHandler.ExpiresAtClaimType, out var expiresAt))
            return Unauthorized(new { message = InvalidTokenMessage });

        return Ok(new VerifyTokenResponse(true, issuedAt, expiresAt));
    }

    [HttpGet("permissions")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> GetPermissions(
        [FromHeader(Name = SystemIdHeader)] string? systemIdHeader)
    {
        var systemValidation = await ValidateSystemHeaderAsync(systemIdHeader);
        if (systemValidation.ErrorResult is not null)
            return systemValidation.ErrorResult;
        var headerSystemId = systemValidation.SystemId;

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = InvalidTokenMessage });

        var sysClaim = User.FindFirstValue(JwtBearerAuthenticationHandler.SystemIdClaimType);
        if (string.IsNullOrEmpty(sysClaim) || !Guid.TryParse(sysClaim, out var tokenSystemId))
            return Unauthorized(new { message = InvalidTokenMessage });

        if (tokenSystemId != headerSystemId)
        {
            _logger.LogWarning(
                "Token cruzado detectado em /auth/permissions: claim sys={TokenSystemId} difere do header X-System-Id={HeaderSystemId}.",
                tokenSystemId,
                headerSystemId);
            return Unauthorized(new { message = CrossSystemTokenMessage });
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, HttpContext.RequestAborted);
        if (user is null)
            return Unauthorized(new { message = UserNotFoundMessage });

        var routes = await ResolveRoutesAsync(headerSystemId, HttpContext.RequestAborted);

        return Ok(new PermissionsResponse(
            new PermissionsUserDto(user.Id, user.Name, user.Email, user.Identity),
            routes));
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

    private readonly record struct SystemHeaderValidation(IActionResult? ErrorResult, Guid SystemId);

    private async Task<SystemHeaderValidation> ValidateSystemHeaderAsync(string? systemIdHeader)
    {
        if (string.IsNullOrWhiteSpace(systemIdHeader))
            return new SystemHeaderValidation(BadRequest(new { message = SystemIdHeaderRequiredMessage }), Guid.Empty);

        if (!Guid.TryParse(systemIdHeader, out var headerSystemId) || headerSystemId == Guid.Empty)
            return new SystemHeaderValidation(BadRequest(new { message = InvalidSystemIdMessage }), Guid.Empty);

        var systemExists = await _db.Systems.AsNoTracking()
            .AnyAsync(s => s.Id == headerSystemId, HttpContext.RequestAborted);
        if (!systemExists)
            return new SystemHeaderValidation(BadRequest(new { message = InvalidSystemIdMessage }), Guid.Empty);

        return new SystemHeaderValidation(null, headerSystemId);
    }

    private bool TryReadUnixSecondsClaim(string claimType, out DateTimeOffset value)
    {
        value = default;
        var raw = User.FindFirstValue(claimType);
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return false;

        value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private async Task<IReadOnlyList<string>> ResolveRoutesAsync(
        Guid systemId,
        CancellationToken cancellationToken)
    {
        return await _db.Routes.AsNoTracking()
            .Where(r => r.SystemId == systemId)
            .Select(r => r.Code)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
    }
}
