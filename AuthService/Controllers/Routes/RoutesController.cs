using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Data;
using AuthService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static AuthService.Helpers.DbExceptionHelper;

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

    private static ConflictObjectResult CodeConflictResult() =>
        new(new { message = "Já existe uma route com este Code." });

    private async Task<bool> SystemExistsAndActiveAsync(Guid systemId) =>
        systemId != Guid.Empty && await _db.Systems.AnyAsync(s => s.Id == systemId);

    /// <summary>Routes ativas cujo sistema pai ainda está ativo (leitura alinhada a POST/PUT).</summary>
    private IQueryable<AppRoute> ActiveRoutesWithActiveSystem() =>
        _db.Routes.Where(r => _db.Systems.Any(s => s.Id == r.SystemId));

    private ActionResult InvalidSystemIdResult()
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

    public class RouteSyncItem
    {
        [Required(ErrorMessage = "Code é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Code deve ter no máximo 50 caracteres.")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description deve ter no máximo 500 caracteres.")]
        public string? Description { get; set; }

        /// <summary>Code do PermissionType (ex.: read/create). Se informado, cria/reativa a Permission(Route, Type).</summary>
        [MaxLength(50, ErrorMessage = "PermissionTypeCode deve ter no máximo 50 caracteres.")]
        public string? PermissionTypeCode { get; set; }
    }

    public class SyncRoutesRequest
    {
        [Required(ErrorMessage = "SystemCode é obrigatório.")]
        [MaxLength(50, ErrorMessage = "SystemCode deve ter no máximo 50 caracteres.")]
        public string SystemCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Routes é obrigatório.")]
        public List<RouteSyncItem> Routes { get; set; } = new();
    }

    public record SyncRoutesResponse(int Created, int Updated, int Reactivated, int Deleted);

    [HttpPost("sync")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesUpdate)]
    public async Task<IActionResult> Sync([FromBody] SyncRoutesRequest request, [FromQuery] bool prune = false)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var duplicates = request.Routes
            .GroupBy(r => r.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            ModelState.AddModelError(nameof(request.Routes),
                $"Codes duplicados no payload: {string.Join(", ", duplicates)}.");
            return ValidationProblem(ModelState);
        }

        var system = await _db.Systems.FirstOrDefaultAsync(s => s.Code == request.SystemCode);
        if (system is null)
            return NotFound(new { message = $"System '{request.SystemCode}' não encontrado." });

        var typeCodes = request.Routes
            .Where(r => !string.IsNullOrWhiteSpace(r.PermissionTypeCode))
            .Select(r => r.PermissionTypeCode!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var typeIdByCode = typeCodes.Count == 0
            ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : await _db.PermissionTypes.AsNoTracking()
                .Where(t => typeCodes.Contains(t.Code))
                .ToDictionaryAsync(t => t.Code, t => t.Id);

        var unknownTypes = typeCodes.Where(c => !typeIdByCode.ContainsKey(c)).ToList();
        if (unknownTypes.Count > 0)
        {
            ModelState.AddModelError(nameof(request.Routes),
                $"PermissionTypeCode desconhecido(s): {string.Join(", ", unknownTypes)}.");
            return ValidationProblem(ModelState);
        }

        var existingRoutes = await _db.Routes.IgnoreQueryFilters()
            .Where(r => r.SystemId == system.Id)
            .ToListAsync();
        var existingByCode = existingRoutes.ToDictionary(r => r.Code, StringComparer.Ordinal);
        var requestCodes = request.Routes.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);

        var utc = DateTime.UtcNow;
        var created = 0;
        var updated = 0;
        var reactivated = 0;
        var deleted = 0;

        foreach (var item in request.Routes)
        {
            Guid routeId;
            if (existingByCode.TryGetValue(item.Code, out var existing))
            {
                var changed = false;
                if (existing.Name != item.Name) { existing.Name = item.Name; changed = true; }
                if (existing.Description != item.Description) { existing.Description = item.Description; changed = true; }
                if (existing.DeletedAt is not null) { existing.DeletedAt = null; reactivated++; }
                if (changed) updated++;
                existing.UpdatedAt = utc;
                routeId = existing.Id;
            }
            else
            {
                var route = new AppRoute
                {
                    SystemId = system.Id,
                    Name = item.Name,
                    Code = item.Code,
                    Description = item.Description,
                    CreatedAt = utc,
                    UpdatedAt = utc,
                    DeletedAt = null
                };
                _db.Routes.Add(route);
                routeId = route.Id;
                created++;
            }

            if (!string.IsNullOrWhiteSpace(item.PermissionTypeCode))
                await EnsurePermissionAsync(routeId, typeIdByCode[item.PermissionTypeCode!], utc);
        }

        if (prune)
        {
            foreach (var existing in existingRoutes.Where(r => !requestCodes.Contains(r.Code) && r.DeletedAt is null))
            {
                existing.DeletedAt = utc;
                existing.UpdatedAt = utc;
                deleted++;

                var perms = await _db.Permissions.IgnoreQueryFilters()
                    .Where(p => p.RouteId == existing.Id && p.DeletedAt == null)
                    .ToListAsync();
                foreach (var p in perms)
                {
                    p.DeletedAt = utc;
                    p.UpdatedAt = utc;
                }
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // UX_Routes_Code é global; um Code do payload pode colidir com outro sistema.
            return Conflict(new { message = "Code de rota já em uso por outro sistema." });
        }

        return Ok(new SyncRoutesResponse(created, updated, reactivated, deleted));
    }

    private async Task EnsurePermissionAsync(Guid routeId, Guid typeId, DateTime utc)
    {
        var existing = await _db.Permissions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.RouteId == routeId && p.PermissionTypeId == typeId);

        if (existing is null)
        {
            _db.Permissions.Add(new AppPermission
            {
                RouteId = routeId,
                PermissionTypeId = typeId,
                CreatedAt = utc,
                UpdatedAt = utc,
                DeletedAt = null
            });
            return;
        }

        if (existing.DeletedAt is not null)
        {
            existing.DeletedAt = null;
            existing.UpdatedAt = utc;
        }
    }
}
