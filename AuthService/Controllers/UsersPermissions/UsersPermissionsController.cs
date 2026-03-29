using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.UsersPermissions;

[ApiController]
[Route("users-permissions")]
public class UsersPermissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersPermissionsController> _logger;

    public UsersPermissionsController(AppDbContext db, ILogger<UsersPermissionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreateUserPermissionRequest
    {
        [Required(ErrorMessage = "UserId é obrigatório.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid PermissionId { get; set; }
    }

    public class UpdateUserPermissionRequest
    {
        [Required(ErrorMessage = "UserId é obrigatório.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid PermissionId { get; set; }
    }

    public record UserPermissionResponse(
        Guid Id,
        Guid UserId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static UserPermissionResponse ToResponse(AppUserPermission e) =>
        new(e.Id, e.UserId, e.PermissionId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

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

    private async Task<bool> PermissionExistsAndActiveAsync(Guid permissionId) =>
        permissionId != Guid.Empty && await _db.Permissions.AnyAsync(p => p.Id == permissionId);

    /// <summary>Vínculos ativos cujo usuário e permissão ainda estão ativos.</summary>
    private IQueryable<AppUserPermission> ActiveUserPermissionsWithActiveParents() =>
        _db.UserPermissions.Where(up =>
            _db.Users.Any(u => u.Id == up.UserId)
            && _db.Permissions.Any(p => p.Id == up.PermissionId));

    private IActionResult InvalidReferencesResult(bool userOk, bool permissionOk)
    {
        if (!userOk)
            ModelState.AddModelError(nameof(CreateUserPermissionRequest.UserId), "UserId inválido ou usuário inativo.");
        if (!permissionOk)
            ModelState.AddModelError(nameof(CreateUserPermissionRequest.PermissionId),
                "PermissionId inválido ou permissão inativa.");
        return ValidationProblem(ModelState);
    }

    private static IActionResult UniquePairConflictResult() =>
        new ConflictObjectResult(new { message = "Já existe vínculo entre este usuário e esta permissão." });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserPermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userOk = await UserExistsAndActiveAsync(request.UserId);
        var permissionOk = await PermissionExistsAndActiveAsync(request.PermissionId);
        if (!userOk || !permissionOk)
        {
            _logger.LogWarning(
                "Criação de users-permissions rejeitada: UserId {UserId} ok={UserOk}, PermissionId {PermissionId} ok={PermOk}.",
                request.UserId, userOk, request.PermissionId, permissionOk);
            return InvalidReferencesResult(userOk, permissionOk);
        }

        if (await _db.UserPermissions.IgnoreQueryFilters()
                .AnyAsync(up => up.UserId == request.UserId && up.PermissionId == request.PermissionId))
        {
            _logger.LogWarning(
                "Conflito ao criar vínculo usuário-permissão: UserId {UserId}, PermissionId {PermissionId}.",
                request.UserId, request.PermissionId);
            return UniquePairConflictResult();
        }

        var now = DateTime.UtcNow;
        var entity = new AppUserPermission
        {
            UserId = request.UserId,
            PermissionId = request.PermissionId,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.UserPermissions.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao criar vínculo UserId {UserId}, PermissionId {PermissionId}.",
                request.UserId, request.PermissionId);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao criar vínculo usuário-permissão.");
            var u = await UserExistsAndActiveAsync(request.UserId);
            var p = await PermissionExistsAndActiveAsync(request.PermissionId);
            return InvalidReferencesResult(u, p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir novo vínculo usuário-permissão.");
            throw;
        }

        _logger.LogInformation("Vínculo usuário-permissão criado: {UserPermissionId}.", entity.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await ActiveUserPermissionsWithActiveParents()
            .OrderBy(up => up.CreatedAt)
            .Select(up => new UserPermissionResponse(
                up.Id,
                up.UserId,
                up.PermissionId,
                up.CreatedAt,
                up.UpdatedAt,
                up.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await ActiveUserPermissionsWithActiveParents().FirstOrDefaultAsync(up => up.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-permissão não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateUserPermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.UserPermissions.FirstOrDefaultAsync(up => up.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-permissão não encontrado." });

        var userOk = await UserExistsAndActiveAsync(request.UserId);
        var permissionOk = await PermissionExistsAndActiveAsync(request.PermissionId);
        if (!userOk || !permissionOk)
        {
            _logger.LogWarning(
                "Atualização de vínculo {UserPermissionId} rejeitada: User ok={UserOk}, Permission ok={PermOk}.",
                id, userOk, permissionOk);
            return InvalidReferencesResult(userOk, permissionOk);
        }

        if (await _db.UserPermissions.IgnoreQueryFilters()
                .AnyAsync(up => up.Id != id && up.UserId == request.UserId && up.PermissionId == request.PermissionId))
        {
            _logger.LogWarning(
                "Conflito ao atualizar vínculo {UserPermissionId}: par UserId/PermissionId já existe.", id);
            return UniquePairConflictResult();
        }

        entity.UserId = request.UserId;
        entity.PermissionId = request.PermissionId;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao atualizar vínculo {UserPermissionId}.", id);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao atualizar vínculo {UserPermissionId}.", id);
            var u = await UserExistsAndActiveAsync(request.UserId);
            var p = await PermissionExistsAndActiveAsync(request.PermissionId);
            return InvalidReferencesResult(u, p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir atualização do vínculo {UserPermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-permissão atualizado: {UserPermissionId}.", id);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.UserPermissions.FirstOrDefaultAsync(up => up.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-permissão não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao excluir (soft) vínculo {UserPermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-permissão excluído (soft): {UserPermissionId}.", id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.UserPermissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(up => up.Id == id && up.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Vínculo usuário-permissão não encontrado ou não está deletado." });

        if (!await UserExistsAndActiveAsync(entity.UserId) || !await PermissionExistsAndActiveAsync(entity.PermissionId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar o vínculo: o usuário ou a permissão vinculada está inativa."
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
            _logger.LogError(ex, "Erro inesperado ao restaurar vínculo {UserPermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo usuário-permissão restaurado: {UserPermissionId}.", id);
        return Ok(ToResponse(entity));
    }
}
