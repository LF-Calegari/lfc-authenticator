using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.Permissions;

[ApiController]
[Route("permissions")]
public partial class PermissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<PermissionsController> _logger;

    public PermissionsController(AppDbContext db, ILogger<PermissionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreatePermissionRequest
    {
        [Required(ErrorMessage = "SystemId é obrigatório.")]
        public Guid? SystemId { get; set; }

        [Required(ErrorMessage = "PermissionTypeId é obrigatório.")]
        public Guid? PermissionTypeId { get; set; }

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public class UpdatePermissionRequest
    {
        [Required(ErrorMessage = "SystemId é obrigatório.")]
        public Guid? SystemId { get; set; }

        [Required(ErrorMessage = "PermissionTypeId é obrigatório.")]
        public Guid? PermissionTypeId { get; set; }

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public record PermissionResponse(
        Guid Id,
        Guid SystemId,
        Guid PermissionTypeId,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static PermissionResponse ToResponse(AppPermission e) =>
        new(e.Id, e.SystemId, e.PermissionTypeId, e.Description, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static void ValidateDescription(ModelStateDictionary modelState, string? descriptionOrNull, string descriptionPropertyKey)
    {
        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(descriptionPropertyKey, "Description deve ter no máximo 500 caracteres.");
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
                return sql.Number == 547;
        }

        return false;
    }

    private async Task<bool> SystemExistsAndActiveAsync(Guid systemId) =>
        systemId != Guid.Empty && await _db.Systems.AnyAsync(s => s.Id == systemId);

    private async Task<bool> PermissionTypeExistsAndActiveAsync(Guid permissionTypeId) =>
        permissionTypeId != Guid.Empty && await _db.PermissionTypes.AnyAsync(t => t.Id == permissionTypeId);

    /// <summary>Permissões ativas cujo sistema e tipo de permissão ainda estão ativos.</summary>
    private IQueryable<AppPermission> ActivePermissionsWithActiveParents() =>
        _db.Permissions.Where(p =>
            _db.Systems.Any(s => s.Id == p.SystemId)
            && _db.PermissionTypes.Any(t => t.Id == p.PermissionTypeId));

    private ActionResult InvalidReferencesResult(bool systemOk, bool typeOk)
    {
        if (!systemOk)
            ModelState.AddModelError(nameof(CreatePermissionRequest.SystemId), "SystemId inválido ou sistema inativo.");
        if (!typeOk)
            ModelState.AddModelError(nameof(CreatePermissionRequest.PermissionTypeId), "PermissionTypeId inválido ou tipo de permissão inativo.");
        return ValidationProblem(ModelState);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.PermissionsCreate)]
    public async Task<IActionResult> Create([FromBody] CreatePermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var systemId = request.SystemId!.Value;
        var permissionTypeId = request.PermissionTypeId!.Value;

        var systemOk = await SystemExistsAndActiveAsync(systemId);
        var typeOk = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
        if (!systemOk || !typeOk)
        {
            _logger.LogWarning(
                "Criação de permissão rejeitada: SystemId {SystemId} ok={SystemOk}, PermissionTypeId {TypeId} ok={TypeOk}.",
                systemId, systemOk, permissionTypeId, typeOk);
            return InvalidReferencesResult(systemOk, typeOk);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ValidateDescription(ModelState, description, nameof(CreatePermissionRequest.Description));
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        var entity = new AppPermission
        {
            SystemId = systemId,
            PermissionTypeId = permissionTypeId,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Permissions.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao criar permissão.");
            var s = await SystemExistsAndActiveAsync(systemId);
            var t = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
            return InvalidReferencesResult(s, t);
        }

        LogPermissionCreated(entity.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.PermissionsRead)]
    public async Task<IActionResult> GetAll()
    {
        var list = await ActivePermissionsWithActiveParents()
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PermissionResponse(
                p.Id,
                p.SystemId,
                p.PermissionTypeId,
                p.Description,
                p.CreatedAt,
                p.UpdatedAt,
                p.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await ActivePermissionsWithActiveParents().FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permissão não encontrada." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdatePermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.Permissions.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permissão não encontrada." });

        var systemId = request.SystemId!.Value;
        var permissionTypeId = request.PermissionTypeId!.Value;

        var systemOk = await SystemExistsAndActiveAsync(systemId);
        var typeOk = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
        if (!systemOk || !typeOk)
        {
            _logger.LogWarning(
                "Atualização de permissão {PermissionId} rejeitada: System ok={SystemOk}, Type ok={TypeOk}.",
                id, systemOk, typeOk);
            return InvalidReferencesResult(systemOk, typeOk);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ValidateDescription(ModelState, description, nameof(UpdatePermissionRequest.Description));
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        entity.SystemId = systemId;
        entity.PermissionTypeId = permissionTypeId;
        entity.Description = description;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _logger.LogWarning(ex, "Violação de FK ao atualizar permissão {PermissionId}.", id);
            var s = await SystemExistsAndActiveAsync(systemId);
            var t = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
            return InvalidReferencesResult(s, t);
        }

        LogPermissionUpdated(id);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.Permissions.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permissão não encontrada." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogPermissionDeleted(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.PermissionsRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.Permissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Permissão não encontrada ou não está deletada." });

        if (!await SystemExistsAndActiveAsync(entity.SystemId) || !await PermissionTypeExistsAndActiveAsync(entity.PermissionTypeId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar a permissão: o sistema ou o tipo de permissão vinculado está inativo."
            });
        }

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogPermissionRestored(id);
        return Ok(new { message = "Permissão restaurada com sucesso." });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Permissão criada: {PermissionId}.")]
    private partial void LogPermissionCreated(Guid permissionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Permissão atualizada: {PermissionId}.")]
    private partial void LogPermissionUpdated(Guid permissionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Permissão excluída (soft): {PermissionId}.")]
    private partial void LogPermissionDeleted(Guid permissionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Permissão restaurada: {PermissionId}.")]
    private partial void LogPermissionRestored(Guid permissionId);
}
