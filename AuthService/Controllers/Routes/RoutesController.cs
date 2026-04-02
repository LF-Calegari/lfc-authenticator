using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Controllers.Routes;

[ApiController]
[Route("systems/routes")]
public class RoutesController : ControllerBase
{
    private readonly AppDbContext _db;

    public RoutesController(AppDbContext db)
    {
        _db = db;
    }

    public class CreateRouteRequest
    {
        [Required(ErrorMessage = "SystemId é obrigatório.")]
        public Guid? SystemId { get; set; }

        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public class UpdateRouteRequest
    {
        [Required(ErrorMessage = "SystemId é obrigatório.")]
        public Guid? SystemId { get; set; }

        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }
    }

    public record RouteResponse(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static RouteResponse ToResponse(AppRoute r) =>
        new(r.Id, r.SystemId, r.Name, r.Code, r.Description, r.CreatedAt, r.UpdatedAt, r.DeletedAt);

    private static void ValidateNormalizedFields(
        ModelStateDictionary modelState,
        string name,
        string code,
        string? descriptionOrNull)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreateRouteRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(code))
            modelState.AddModelError(nameof(CreateRouteRequest.Code), "Code é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreateRouteRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (code.Length > 50)
            modelState.AddModelError(nameof(CreateRouteRequest.Code), "Code deve ter no máximo 50 caracteres.");

        if (descriptionOrNull is { Length: > 500 })
            modelState.AddModelError(nameof(CreateRouteRequest.Description), "Description deve ter no máximo 500 caracteres.");
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

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
                return sql.Number == 547;
        }

        return false;
    }

    private static IEnumerable<string> GetExceptionMessages(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            yield return e.Message;
    }

    private static IActionResult CodeConflictResult() =>
        new ConflictObjectResult(new { message = "Já existe uma route com este Code." });

    private async Task<bool> SystemExistsAndActiveAsync(Guid systemId) =>
        systemId != Guid.Empty && await _db.Systems.AnyAsync(s => s.Id == systemId);

    /// <summary>Routes ativas cujo sistema pai ainda está ativo (leitura alinhada a POST/PUT).</summary>
    private IQueryable<AppRoute> ActiveRoutesWithActiveSystem() =>
        _db.Routes.Where(r => _db.Systems.Any(s => s.Id == r.SystemId));

    private IActionResult InvalidSystemIdResult()
    {
        ModelState.AddModelError(nameof(CreateRouteRequest.SystemId), "SystemId inválido ou sistema inativo.");
        return ValidationProblem(ModelState);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateRouteRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var systemId = request.SystemId!.Value;

        if (!await SystemExistsAndActiveAsync(systemId))
            return InvalidSystemIdResult();

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Routes.IgnoreQueryFilters().AnyAsync(r => r.Code == code))
            return CodeConflictResult();

        var now = DateTime.UtcNow;
        var entity = new AppRoute
        {
            SystemId = systemId,
            Name = name,
            Code = code,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Routes.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CodeConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return InvalidSystemIdResult();
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesRead)]
    public async Task<IActionResult> GetAll()
    {
        var list = await ActiveRoutesWithActiveSystem()
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RouteResponse(
                r.Id,
                r.SystemId,
                r.Name,
                r.Code,
                r.Description,
                r.CreatedAt,
                r.UpdatedAt,
                r.DeletedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var entity = await ActiveRoutesWithActiveSystem().FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Route não encontrada." });
        return Ok(ToResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateRouteRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Route não encontrada." });

        var systemId = request.SystemId!.Value;

        if (!await SystemExistsAndActiveAsync(systemId))
            return InvalidSystemIdResult();

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        ValidateNormalizedFields(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Routes.IgnoreQueryFilters().AnyAsync(r => r.Id != id && r.Code == code))
            return new ConflictObjectResult(new { message = "Já existe outra route com este Code." });

        entity.SystemId = systemId;
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
            return new ConflictObjectResult(new { message = "Já existe outra route com este Code." });
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return InvalidSystemIdResult();
        }

        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return NotFound(new { message = "Route não encontrada." });

        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var entity = await _db.Routes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null);

        if (entity is null)
            return NotFound(new { message = "Route não encontrada ou não está deletada." });

        if (!await SystemExistsAndActiveAsync(entity.SystemId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar a route: o sistema vinculado está inativo ou foi removido."
            });
        }

        entity.DeletedAt = null;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Route restaurada com sucesso." });
    }
}
