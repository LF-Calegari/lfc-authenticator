using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static AuthService.Helpers.DbExceptionHelper;

namespace AuthService.Controllers.PermissionTypes;

[ApiController]
[Route("permissions/types")]
public class PermissionTypesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PermissionTypesController(AppDbContext db)
    {
        _db = db;
    }

    public class CreatePermissionTypeRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public class UpdatePermissionTypeRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public record PermissionTypeResponse(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static PermissionTypeResponse ToResponse(AppPermissionType e) =>
        new(e.Id, e.Name, e.Code, e.Description, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static void ValidateNormalizedFields(
        ModelStateDictionary modelState,
        string name,
        string code,
        string? descriptionOrNull)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreatePermissionTypeRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(nameof(CreatePermissionTypeRequest.Code), "Code é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreatePermissionTypeRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (code.Length > 50)
            modelState.AddModelError(nameof(CreatePermissionTypeRequest.Code), "Code deve ter no máximo 50 caracteres.");

        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(nameof(CreatePermissionTypeRequest.Description), "Description deve ter no máximo 500 caracteres.");
    }

    private static ConflictObjectResult UniqueConflictResult() =>
        new(new { message = "Já existe um permission type com este Code." });

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesCreate)]
    public async Task<IActionResult> Create([FromBody] CreatePermissionTypeRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.PermissionTypes.IgnoreQueryFilters().AnyAsync(p => p.Code == code))
            return UniqueConflictResult();

        var now = DateTime.UtcNow;
        var entity = new AppPermissionType
        {
            Name = name,
            Code = code,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.PermissionTypes.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return UniqueConflictResult();
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesRead)]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.PermissionTypes
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PermissionTypeResponse(
                p.Id,
                p.Name,
                p.Code,
                p.Description,
                p.CreatedAt,
                p.UpdatedAt,
                p.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await _db.PermissionTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permission type não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdatePermissionTypeRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.PermissionTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permission type não encontrado." });

        if (await _db.PermissionTypes.IgnoreQueryFilters().AnyAsync(p => p.Id != id && p.Code == code))
            return new ConflictObjectResult(new { message = "Já existe outro permission type com este Code." });

        entity.Name = name;
        entity.Code = code;
        entity.Description = description;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return new ConflictObjectResult(new { message = "Já existe outro permission type com este Code." });
        }

        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.PermissionTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Permission type não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.PermissionsTypesRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.PermissionTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Permission type não encontrado ou não está deletado." });

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Permission type restaurado com sucesso." });
    }
}
