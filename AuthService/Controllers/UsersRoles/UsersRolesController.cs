using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.UsersRoles;

[ApiController]
[Route("users-roles")]
public class UsersRolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersRolesController> _logger;

    public UsersRolesController(AppDbContext db, ILogger<UsersRolesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreateUserRoleRequest
    {
        [Required(ErrorMessage = "UserId é obrigatório.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "RoleId é obrigatório.")]
        public Guid RoleId { get; set; }
    }

    public class UpdateUserRoleRequest
    {
        [Required(ErrorMessage = "UserId é obrigatório.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "RoleId é obrigatório.")]
        public Guid RoleId { get; set; }
    }

    public record UserRoleResponse(
        int Id,
        Guid UserId,
        Guid RoleId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static UserRoleResponse ToResponse(AppUserRole e) =>
        new(e.Id, e.UserId, e.RoleId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
                return sql.Number == 547;
        }

        return false;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
                return sql.Number is 2601 or 2627;
        }

        var text = string.Join(" ", GetExceptionMessages(ex));
        return text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
               || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetExceptionMessages(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            yield return e.Message;
    }

    private async Task<bool> UserExistsAndActiveAsync(Guid userId) =>
        userId != Guid.Empty && await _db.Users.AnyAsync(u => u.Id == userId);

    private async Task<bool> RoleExistsAndActiveAsync(Guid roleId) =>
        roleId != Guid.Empty && await _db.Roles.AnyAsync(r => r.Id == roleId);

    /// <summary>Vínculos ativos cujo usuário e papel ainda estão ativos.</summary>
    private IQueryable<AppUserRole> ActiveUserRolesWithActiveParents() =>
        _db.UserRoles.Where(ur =>
            _db.Users.Any(u => u.Id == ur.UserId)
            && _db.Roles.Any(r => r.Id == ur.RoleId));

    private IActionResult InvalidReferencesResult(bool userOk, bool roleOk)
    {
        if (!userOk)
            ModelState.AddModelError(nameof(CreateUserRoleRequest.UserId), "UserId inválido ou usuário inativo.");
        if (!roleOk)
            ModelState.AddModelError(nameof(CreateUserRoleRequest.RoleId), "RoleId inválido ou papel inativo.");
        return ValidationProblem(ModelState);
    }

    private static IActionResult UniquePairConflictResult() =>
        new ConflictObjectResult(new { message = "Já existe vínculo entre este usuário e este papel." });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userOk = await UserExistsAndActiveAsync(request.UserId);
        var roleOk = await RoleExistsAndActiveAsync(request.RoleId);
        if (!userOk || !roleOk)
        {
            _logger.LogWarning(
                "Criação de users-roles rejeitada: UserId {UserId} ok={UserOk}, RoleId {RoleId} ok={RoleOk}.",
                request.UserId, userOk, request.RoleId, roleOk);
            return InvalidReferencesResult(userOk, roleOk);
        }

        if (await _db.UserRoles.IgnoreQueryFilters()
                .AnyAsync(ur => ur.UserId == request.UserId && ur.RoleId == request.RoleId))
        {
            _logger.LogWarning(
                "Conflito ao criar vínculo usuário-papel: UserId {UserId}, RoleId {RoleId}.",
                request.UserId, request.RoleId);
            return UniquePairConflictResult();
        }

        var now = DateTime.UtcNow;
        var entity = new AppUserRole
        {
            UserId = request.UserId,
            RoleId = request.RoleId,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.UserRoles.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao criar vínculo UserId {UserId}, RoleId {RoleId}.",
                request.UserId, request.RoleId);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao criar vínculo usuário-papel.");
            var u = await UserExistsAndActiveAsync(request.UserId);
            var r = await RoleExistsAndActiveAsync(request.RoleId);
            return InvalidReferencesResult(u, r);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir novo vínculo usuário-papel.");
            throw;
        }

        _logger.LogInformation("Vínculo usuário-papel criado: {UserRoleId}.", entity.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await ActiveUserRolesWithActiveParents()
            .OrderBy(ur => ur.CreatedAt)
            .Select(ur => new UserRoleResponse(
                ur.Id,
                ur.UserId,
                ur.RoleId,
                ur.CreatedAt,
                ur.UpdatedAt,
                ur.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await ActiveUserRolesWithActiveParents().FirstOrDefaultAsync(ur => ur.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-papel não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateById(int id, [FromBody] UpdateUserRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.UserRoles.FirstOrDefaultAsync(ur => ur.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-papel não encontrado." });

        var userOk = await UserExistsAndActiveAsync(request.UserId);
        var roleOk = await RoleExistsAndActiveAsync(request.RoleId);
        if (!userOk || !roleOk)
        {
            _logger.LogWarning(
                "Atualização de vínculo {UserRoleId} rejeitada: User ok={UserOk}, Role ok={RoleOk}.",
                id, userOk, roleOk);
            return InvalidReferencesResult(userOk, roleOk);
        }

        if (await _db.UserRoles.IgnoreQueryFilters()
                .AnyAsync(ur => ur.Id != id && ur.UserId == request.UserId && ur.RoleId == request.RoleId))
        {
            _logger.LogWarning(
                "Conflito ao atualizar vínculo {UserRoleId}: par UserId/RoleId já existe.", id);
            return UniquePairConflictResult();
        }

        entity.UserId = request.UserId;
        entity.RoleId = request.RoleId;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao atualizar vínculo {UserRoleId}.", id);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao atualizar vínculo {UserRoleId}.", id);
            var u = await UserExistsAndActiveAsync(request.UserId);
            var r = await RoleExistsAndActiveAsync(request.RoleId);
            return InvalidReferencesResult(u, r);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir atualização do vínculo {UserRoleId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-papel atualizado: {UserRoleId}.", id);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteById(int id)
    {
        var entity = await _db.UserRoles.FirstOrDefaultAsync(ur => ur.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-papel não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao excluir (soft) vínculo {UserRoleId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-papel excluído (soft): {UserRoleId}.", id);
        return NoContent();
    }

    [HttpPatch("{id:int}/restore")]
    public async Task<IActionResult> RestoreById(int id)
    {
        var entity = await _db.UserRoles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ur => ur.Id == id && ur.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-papel não encontrado ou não está deletado." });

        if (!await UserExistsAndActiveAsync(entity.UserId) || !await RoleExistsAndActiveAsync(entity.RoleId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar o vínculo: o usuário ou o papel vinculado está inativo."
            });
        }

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao restaurar vínculo {UserRoleId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-papel restaurado: {UserRoleId}.", id);
        return Ok(ToResponse(entity));
    }
}
