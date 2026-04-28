using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static AuthService.Helpers.DbExceptionHelper;

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
        [Required(ErrorMessage = "RouteId é obrigatório.")]
        public Guid? RouteId { get; set; }

        [Required(ErrorMessage = "PermissionTypeId é obrigatório.")]
        public Guid? PermissionTypeId { get; set; }

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public class UpdatePermissionRequest
    {
        [Required(ErrorMessage = "RouteId é obrigatório.")]
        public Guid? RouteId { get; set; }

        [Required(ErrorMessage = "PermissionTypeId é obrigatório.")]
        public Guid? PermissionTypeId { get; set; }

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public record PermissionResponse(
        Guid Id,
        Guid RouteId,
        Guid PermissionTypeId,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static PermissionResponse ToResponse(AppPermission e) =>
        new(e.Id, e.RouteId, e.PermissionTypeId, e.Description, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static void ValidateDescription(ModelStateDictionary modelState, string? descriptionOrNull, string descriptionPropertyKey)
    {
        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(descriptionPropertyKey, "Description deve ter no máximo 500 caracteres.");
    }

    private async Task<bool> RouteExistsAndActiveAsync(Guid routeId) =>
        routeId != Guid.Empty && await _db.Routes.AnyAsync(r => r.Id == routeId);

    private async Task<bool> PermissionTypeExistsAndActiveAsync(Guid permissionTypeId) =>
        permissionTypeId != Guid.Empty && await _db.PermissionTypes.AnyAsync(t => t.Id == permissionTypeId);

    /// <summary>Permissões ativas cuja rota e tipo de permissão ainda estão ativos.</summary>
    private IQueryable<AppPermission> ActivePermissionsWithActiveParents() =>
        _db.Permissions.Where(p =>
            _db.Routes.Any(r => r.Id == p.RouteId)
            && _db.PermissionTypes.Any(t => t.Id == p.PermissionTypeId));

    private ActionResult InvalidReferencesResult(bool routeOk, bool typeOk)
    {
        if (!routeOk)
            ModelState.AddModelError(nameof(CreatePermissionRequest.RouteId), "RouteId inválido ou rota inativa.");
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

        var routeId = request.RouteId!.Value;
        var permissionTypeId = request.PermissionTypeId!.Value;

        var routeOk = await RouteExistsAndActiveAsync(routeId);
        var typeOk = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
        if (!routeOk || !typeOk)
        {
            _logger.LogWarning(
                "Criação de permissão rejeitada: RouteId {RouteId} ok={RouteOk}, PermissionTypeId {TypeId} ok={TypeOk}.",
                routeId, routeOk, permissionTypeId, typeOk);
            return InvalidReferencesResult(routeOk, typeOk);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ValidateDescription(ModelState, description, nameof(CreatePermissionRequest.Description));
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        var entity = new AppPermission
        {
            RouteId = routeId,
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
            var r = await RouteExistsAndActiveAsync(routeId);
            var t = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
            return InvalidReferencesResult(r, t);
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
                p.RouteId,
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

        var routeId = request.RouteId!.Value;
        var permissionTypeId = request.PermissionTypeId!.Value;

        var routeOk = await RouteExistsAndActiveAsync(routeId);
        var typeOk = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
        if (!routeOk || !typeOk)
        {
            _logger.LogWarning(
                "Atualização de permissão {PermissionId} rejeitada: Route ok={RouteOk}, Type ok={TypeOk}.",
                id, routeOk, typeOk);
            return InvalidReferencesResult(routeOk, typeOk);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ValidateDescription(ModelState, description, nameof(UpdatePermissionRequest.Description));
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        entity.RouteId = routeId;
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
            var r = await RouteExistsAndActiveAsync(routeId);
            var t = await PermissionTypeExistsAndActiveAsync(permissionTypeId);
            return InvalidReferencesResult(r, t);
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

        if (!await RouteExistsAndActiveAsync(entity.RouteId) || !await PermissionTypeExistsAndActiveAsync(entity.PermissionTypeId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar a permissão: a rota ou o tipo de permissão vinculado está inativo."
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
