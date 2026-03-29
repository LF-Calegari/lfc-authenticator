using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.TokenTypes;

[ApiController]
[Route("tokens/types")]
public class TokenTypesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TokenTypesController(AppDbContext db)
    {
        _db = db;
    }

    public class CreateTokenTypeRequest
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

    public class UpdateTokenTypeRequest
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

    public record TokenTypeResponse(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static TokenTypeResponse ToResponse(AppSystemTokenType e) =>
        new(e.Id, e.Name, e.Code, e.Description, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static void ValidateNormalizedFields(
        ModelStateDictionary modelState,
        string name,
        string code,
        string? descriptionOrNull)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreateTokenTypeRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(nameof(CreateTokenTypeRequest.Code), "Code é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreateTokenTypeRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (code.Length > 50)
            modelState.AddModelError(nameof(CreateTokenTypeRequest.Code), "Code deve ter no máximo 50 caracteres.");

        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(nameof(CreateTokenTypeRequest.Description), "Description deve ter no máximo 500 caracteres.");
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

    private static IActionResult UniqueConflictResult() =>
        new ConflictObjectResult(new { message = "Já existe um token type com este Code." });

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateTokenTypeRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.SystemTokenTypes.IgnoreQueryFilters().AnyAsync(p => p.Code == code))
            return UniqueConflictResult();

        var now = DateTime.UtcNow;
        var entity = new AppSystemTokenType
        {
            Name = name,
            Code = code,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.SystemTokenTypes.Add(entity);

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
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesRead)]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.SystemTokenTypes
            .OrderBy(p => p.CreatedAt)
            .Select(p => new TokenTypeResponse(
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
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await _db.SystemTokenTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Token type não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateTokenTypeRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.SystemTokenTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Token type não encontrado." });

        if (await _db.SystemTokenTypes.IgnoreQueryFilters().AnyAsync(p => p.Id != id && p.Code == code))
            return new ConflictObjectResult(new { message = "Já existe outro token type com este Code." });

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
            return new ConflictObjectResult(new { message = "Já existe outro token type com este Code." });
        }

        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.SystemTokenTypes.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null)
            return NotFound(new { message = "Token type não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.SystemTokensTypesRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.SystemTokenTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Token type não encontrado ou não está deletado." });

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(entity));
    }
}
