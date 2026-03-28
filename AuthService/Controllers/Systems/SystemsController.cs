using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.Systems;

[ApiController]
[Route("systems")]
public class SystemsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SystemsController(AppDbContext db)
    {
        _db = db;
    }

    public class CreateSystemRequest
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

    public class UpdateSystemRequest
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

    public record SystemResponse(
        Guid Id,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static SystemResponse ToResponse(AppSystem s) =>
        new(s.Id, s.Name, s.Code, s.Description, s.CreatedAt, s.UpdatedAt, s.DeletedAt);

    private static void ValidateNormalizedFields(
        ModelStateDictionary modelState,
        string name,
        string code,
        string? descriptionOrNull)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreateSystemRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(nameof(CreateSystemRequest.Code), "Code é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreateSystemRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (code.Length > 50)
            modelState.AddModelError(nameof(CreateSystemRequest.Code), "Code deve ter no máximo 50 caracteres.");

        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(nameof(CreateSystemRequest.Description), "Description deve ter no máximo 500 caracteres.");
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
        new ConflictObjectResult(new { message = "Já existe um sistema com este Code." });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSystemRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Systems.IgnoreQueryFilters().AnyAsync(s => s.Code == code))
            return UniqueConflictResult();

        var now = DateTime.UtcNow;
        var entity = new AppSystem
        {
            Name = name,
            Code = code,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Systems.Add(entity);

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
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Systems
            .OrderBy(s => s.CreatedAt)
            .Select(s => new SystemResponse(
                s.Id,
                s.Name,
                s.Code,
                s.Description,
                s.CreatedAt,
                s.UpdatedAt,
                s.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await _db.Systems.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(new { message = "Sistema não encontrado." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateSystemRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.Systems.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(new { message = "Sistema não encontrado." });

        if (await _db.Systems.IgnoreQueryFilters().AnyAsync(s => s.Id != id && s.Code == code))
            return new ConflictObjectResult(new { message = "Já existe outro sistema com este Code." });

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
            return new ConflictObjectResult(new { message = "Já existe outro sistema com este Code." });
        }

        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.Systems.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(new { message = "Sistema não encontrado." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.Systems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Sistema não encontrado ou não está deletado." });

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(entity));
    }
}
