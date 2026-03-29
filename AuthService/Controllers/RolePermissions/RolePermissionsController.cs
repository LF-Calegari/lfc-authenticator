using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.RolePermissions;

[ApiController]
[Route("roles-permissions")]
public class RolePermissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<RolePermissionsController> _logger;

    public RolePermissionsController(AppDbContext db, ILogger<RolePermissionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreateRolePermissionRequest
    {
        [Required(ErrorMessage = "RoleId é obrigatório.")]
        public Guid RoleId { get; set; }

        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid PermissionId { get; set; }
    }

    public class UpdateRolePermissionRequest
    {
        [Required(ErrorMessage = "RoleId é obrigatório.")]
        public Guid RoleId { get; set; }

        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid PermissionId { get; set; }
    }

    public record RolePermissionResponse(
        Guid Id,
        Guid RoleId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static RolePermissionResponse ToResponse(AppRolePermission e) =>
        new(e.Id, e.RoleId, e.PermissionId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

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

    private enum RoleRefStatus
    {
        Ok,
        NotFoundOrRemoved
    }

    private async Task<RoleRefStatus> GetRoleRefStatusAsync(Guid roleId)
    {
        if (roleId == Guid.Empty)
            return RoleRefStatus.NotFoundOrRemoved;

        var row = await _db.Roles.IgnoreQueryFilters()
            .Where(r => r.Id == roleId)
            .Select(r => new { r.DeletedAt })
            .FirstOrDefaultAsync();

        if (row is null)
            return RoleRefStatus.NotFoundOrRemoved;
        if (row.DeletedAt != null)
            return RoleRefStatus.NotFoundOrRemoved;
        return RoleRefStatus.Ok;
    }

    private async Task<bool> PermissionExistsAndActiveAsync(Guid permissionId) =>
        permissionId != Guid.Empty && await _db.Permissions.AnyAsync(p => p.Id == permissionId);

    /// <summary>Vínculos ativos cujo papel e permissão ainda estão ativos.</summary>
    private IQueryable<AppRolePermission> ActiveRolePermissionsWithActiveParents() =>
        _db.RolePermissions.Where(rp =>
            _db.Roles.Any(r => r.Id == rp.RoleId)
            && _db.Permissions.Any(p => p.Id == rp.PermissionId));

    private IActionResult InvalidReferencesResult(
        string roleIdKey,
        string permissionIdKey,
        RoleRefStatus roleStatus,
        bool permissionOk)
    {
        if (roleStatus != RoleRefStatus.Ok)
        {
            ModelState.AddModelError(roleIdKey,
                "RoleId inválido ou papel não encontrado.");
        }

        if (!permissionOk)
        {
            ModelState.AddModelError(permissionIdKey,
                "PermissionId inválido ou permissão inativa.");
        }

        return ValidationProblem(ModelState);
    }

    private IActionResult InvalidReferencesResultForCreate(RoleRefStatus roleStatus, bool permissionOk) =>
        InvalidReferencesResult(
            nameof(CreateRolePermissionRequest.RoleId),
            nameof(CreateRolePermissionRequest.PermissionId),
            roleStatus,
            permissionOk);

    private IActionResult InvalidReferencesResultForUpdate(RoleRefStatus roleStatus, bool permissionOk) =>
        InvalidReferencesResult(
            nameof(UpdateRolePermissionRequest.RoleId),
            nameof(UpdateRolePermissionRequest.PermissionId),
            roleStatus,
            permissionOk);

    private static IActionResult UniquePairConflictResult() =>
        new ConflictObjectResult(new { message = "Já existe vínculo entre este papel e esta permissão." });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRolePermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var roleStatus = await GetRoleRefStatusAsync(request.RoleId);
        var permissionOk = await PermissionExistsAndActiveAsync(request.PermissionId);
        if (roleStatus != RoleRefStatus.Ok || !permissionOk)
        {
            _logger.LogWarning(
                "Criação de roles-permissions rejeitada: RoleId {RoleId} status={RoleStatus}, PermissionId {PermissionId} ok={PermOk}.",
                request.RoleId, roleStatus, request.PermissionId, permissionOk);
            return InvalidReferencesResultForCreate(roleStatus, permissionOk);
        }

        if (await _db.RolePermissions.IgnoreQueryFilters()
                .AnyAsync(rp => rp.RoleId == request.RoleId && rp.PermissionId == request.PermissionId))
        {
            _logger.LogWarning(
                "Conflito ao criar vínculo papel-permissão: RoleId {RoleId}, PermissionId {PermissionId}.",
                request.RoleId, request.PermissionId);
            return UniquePairConflictResult();
        }

        var now = DateTime.UtcNow;
        var entity = new AppRolePermission
        {
            RoleId = request.RoleId,
            PermissionId = request.PermissionId,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.RolePermissions.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao criar vínculo RoleId {RoleId}, PermissionId {PermissionId}.",
                request.RoleId, request.PermissionId);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao criar vínculo papel-permissão.");
            var r = await GetRoleRefStatusAsync(request.RoleId);
            var p = await PermissionExistsAndActiveAsync(request.PermissionId);
            return InvalidReferencesResultForCreate(r, p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir novo vínculo papel-permissão.");
            throw;
        }

        _logger.LogInformation("Vínculo papel-permissão criado: {RolePermissionId}.", entity.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await ActiveRolePermissionsWithActiveParents()
            .OrderBy(rp => rp.CreatedAt)
            .Select(rp => new RolePermissionResponse(
                rp.Id,
                rp.RoleId,
                rp.PermissionId,
                rp.CreatedAt,
                rp.UpdatedAt,
                rp.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await ActiveRolePermissionsWithActiveParents().FirstOrDefaultAsync(rp => rp.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo papel-permissão não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateRolePermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.RolePermissions.FirstOrDefaultAsync(rp => rp.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo papel-permissão não encontrado." });

        var roleStatus = await GetRoleRefStatusAsync(request.RoleId);
        var permissionOk = await PermissionExistsAndActiveAsync(request.PermissionId);
        if (roleStatus != RoleRefStatus.Ok || !permissionOk)
        {
            _logger.LogWarning(
                "Atualização de vínculo {RolePermissionId} rejeitada: Role status={RoleStatus}, Permission ok={PermOk}.",
                id, roleStatus, permissionOk);
            return InvalidReferencesResultForUpdate(roleStatus, permissionOk);
        }

        if (await _db.RolePermissions.IgnoreQueryFilters()
                .AnyAsync(rp => rp.Id != id && rp.RoleId == request.RoleId && rp.PermissionId == request.PermissionId))
        {
            _logger.LogWarning(
                "Conflito ao atualizar vínculo {RolePermissionId}: par RoleId/PermissionId já existe.", id);
            return UniquePairConflictResult();
        }

        entity.RoleId = request.RoleId;
        entity.PermissionId = request.PermissionId;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao atualizar vínculo {RolePermissionId}.", id);
            return UniquePairConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao atualizar vínculo {RolePermissionId}.", id);
            var r = await GetRoleRefStatusAsync(request.RoleId);
            var p = await PermissionExistsAndActiveAsync(request.PermissionId);
            return InvalidReferencesResultForUpdate(r, p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao persistir atualização do vínculo {RolePermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo papel-permissão atualizado: {RolePermissionId}.", id);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.RolePermissions.FirstOrDefaultAsync(rp => rp.Id == id);
        if (entity is null)
            return NotFound(new { message = "Vínculo papel-permissão não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao excluir (soft) vínculo {RolePermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo papel-permissão excluído (soft): {RolePermissionId}.", id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.RolePermissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rp => rp.Id == id && rp.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Vínculo papel-permissão não encontrado ou não está deletado." });

        if (await GetRoleRefStatusAsync(entity.RoleId) != RoleRefStatus.Ok
            || !await PermissionExistsAndActiveAsync(entity.PermissionId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar o vínculo: o papel ou a permissão vinculada não está mais disponível."
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
            _logger.LogError(ex, "Erro inesperado ao restaurar vínculo {RolePermissionId}.", id);
            throw;
        }

        _logger.LogInformation("Vínculo papel-permissão restaurado: {RolePermissionId}.", id);
        return Ok(ToResponse(entity));
    }
}
