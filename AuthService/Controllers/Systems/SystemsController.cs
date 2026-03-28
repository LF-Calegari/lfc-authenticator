using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSystemRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Systems.IgnoreQueryFilters().AnyAsync(s => s.Code == request.Code))
            return Conflict(new { message = "Já existe um sistema com este Code." });

        var now = DateTime.UtcNow;
        var entity = new AppSystem
        {
            Name = request.Name.Trim(),
            Code = request.Code.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Systems.Add(entity);
        await _db.SaveChangesAsync();
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

        var entity = await _db.Systems.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(new { message = "Sistema não encontrado." });

        if (await _db.Systems.IgnoreQueryFilters().AnyAsync(s => s.Id != id && s.Code == request.Code))
            return Conflict(new { message = "Já existe outro sistema com este Code." });

        entity.Name = request.Name.Trim();
        entity.Code = request.Code.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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
