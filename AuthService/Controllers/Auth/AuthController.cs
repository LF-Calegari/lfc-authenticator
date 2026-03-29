using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserEntity = AuthService.Models.User;

namespace AuthService.Controllers.Auth;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
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

    public record LoginResponse(string Token, DateTime ExpiresAtUtc);

    public record AuthUserResponse(
        Guid Id,
        string Name,
        string Email,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public record VerifyTokenResponse(AuthUserResponse User, IReadOnlyList<Guid> PermissionIds);

    private static AuthUserResponse ToAuthUser(UserEntity u) =>
        new(u.Id, u.Name, u.Email, u.Identity, u.Active, u.CreatedAt, u.UpdatedAt);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var email = request.Email.Trim().ToLowerInvariant();
        var password = request.Password.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Unauthorized(new { message = "Credenciais inválidas." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            _logger.LogWarning("Tentativa de login falhou para o email {Email}.", email);
            return Unauthorized(new { message = "Credenciais inválidas." });
        }

        var (passwordOk, newStoredPassword) = UserPasswordHasher.Verify(user, password);
        if (!passwordOk)
        {
            _logger.LogWarning("Tentativa de login falhou para o email {Email}.", email);
            return Unauthorized(new { message = "Credenciais inválidas." });
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
            return Unauthorized(new { message = "Credenciais inválidas." });
        }

        var token = _jwt.CreateAccessToken(user.Id, user.TokenVersion, out var expiresAt);
        return Ok(new LoginResponse(token, expiresAt.UtcDateTime));
    }

    [HttpGet("verify-token")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> VerifyToken()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = "Token inválido." });

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Unauthorized(new { message = "Usuário não encontrado." });

        var permissionIds = (await EffectivePermissionIds.GetForUserAsync(_db, userId))
            .OrderBy(x => x)
            .ToList();
        return Ok(new VerifyTokenResponse(ToAuthUser(user), permissionIds));
    }

    [HttpGet("logout")]
    [Authorize(AuthenticationSchemes = BearerAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Logout()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var userId))
            return Unauthorized(new { message = "Token inválido." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Unauthorized(new { message = "Usuário não encontrado." });

        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Logout concluído para o usuário {UserId}.", userId);
        return Ok(new { message = "Sessão encerrada." });
    }
}
