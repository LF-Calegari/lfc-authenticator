using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.Roles;

[ApiController]
[Route("roles")]
public partial class RolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<RolesController> _logger;

    public RolesController(AppDbContext db, ILogger<RolesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreateRoleRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;
    }

    public class UpdateRoleRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;
    }

    public record RoleResponse(
        Guid Id,
        string Name,
        string Code,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static RoleResponse ToResponse(AppRole e) =>
        new(e.Id, e.Name, e.Code, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static void ValidateNormalizedFields(
        ModelStateDictionary modelState,
        string name,
        string code)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreateRoleRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(nameof(CreateRoleRequest.Code), "Code é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreateRoleRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (code.Length > 50)
            modelState.AddModelError(nameof(CreateRoleRequest.Code), "Code deve ter no máximo 50 caracteres.");
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

    private static ConflictObjectResult UniqueConflictResult() =>
        new(new { message = "Já existe um role com este Code." });

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.RolesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        ValidateNormalizedFields(ModelState, name, code);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Code == code))
        {
            _logger.LogWarning("Conflito ao criar role: já existe Code {Code}.", code);
            return UniqueConflictResult();
        }

        var now = DateTime.UtcNow;
        var entity = new AppRole
        {
            Name = name,
            Code = code,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Roles.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao criar role com Code {Code}.", code);
            return UniqueConflictResult();
        }

        LogRoleCreated(entity.Id, code);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.RolesRead)]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Roles
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RoleResponse(
                r.Id,
                r.Name,
                r.Code,
                r.CreatedAt,
                r.UpdatedAt,
                r.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Role não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        ValidateNormalizedFields(ModelState, name, code);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Role não encontrado." });

        if (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id != id && r.Code == code))
        {
            _logger.LogWarning("Conflito ao atualizar role {RoleId}: Code {Code} já em uso.", id, code);
            return new ConflictObjectResult(new { message = "Já existe outro role com este Code." });
        }

        entity.Name = name;
        entity.Code = code;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflito de unicidade ao atualizar role {RoleId} para Code {Code}.", id, code);
            return new ConflictObjectResult(new { message = "Já existe outro role com este Code." });
        }

        LogRoleUpdated(id);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Role não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogRoleDeleted(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.RolesRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Role não encontrado ou não está deletado." });

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogRoleRestored(id);
        return Ok(new { message = "Role restaurado com sucesso." });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Role criado: {RoleId}, Code {Code}.")]
    private partial void LogRoleCreated(Guid roleId, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role atualizado: {RoleId}.")]
    private partial void LogRoleUpdated(Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role excluído (soft): {RoleId}.")]
    private partial void LogRoleDeleted(Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role restaurado: {RoleId}.")]
    private partial void LogRoleRestored(Guid roleId);
}
