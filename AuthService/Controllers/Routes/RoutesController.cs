using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
using AuthService.Controllers.Common;
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

        [Required(ErrorMessage = "SystemTokenTypeId é obrigatório.")]
        public Guid? SystemTokenTypeId { get; set; }
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

        [Required(ErrorMessage = "SystemTokenTypeId é obrigatório.")]
        public Guid? SystemTokenTypeId { get; set; }
    }

    public record RouteResponse(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        Guid SystemTokenTypeId,
        string SystemTokenTypeCode,
        string SystemTokenTypeName,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

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

    private async Task<bool> SystemTokenTypeExistsAndActiveAsync(Guid systemTokenTypeId) =>
        systemTokenTypeId != Guid.Empty
        && await _db.SystemTokenTypes.AnyAsync(t => t.Id == systemTokenTypeId);

    /// <summary>Routes ativas cujo sistema pai ainda está ativo (leitura alinhada a POST/PUT).</summary>
    private IQueryable<AppRoute> ActiveRoutesWithActiveSystem() =>
        _db.Routes.Where(r => _db.Systems.Any(s => s.Id == r.SystemId));

    /// <summary>
    /// Projeção do response com Join (left) para denormalizar Code/Name do SystemTokenType. Não usa
    /// <c>IgnoreQueryFilters()</c> em DbSets aninhadas para evitar o efeito colateral do EF Core de
    /// propagar o "ignore" para os operadores de raiz (que vazaria rotas soft-deletadas). Mantemos o
    /// vínculo via Join com a tabela ATIVA de SystemTokenTypes — quando o SystemTokenType referenciado
    /// foi soft-deletado pós-creation, o response mostra strings vazias para Code/Name (Left Join),
    /// e o controller bloqueia o restore via <see cref="SystemTokenTypeExistsAndActiveAsync"/>.
    /// </summary>
    private IQueryable<RouteResponse> ProjectRouteResponses(IQueryable<AppRoute> source) =>
        from r in source
        join t in _db.SystemTokenTypes on r.SystemTokenTypeId equals t.Id into tg
        from t in tg.DefaultIfEmpty()
        select new RouteResponse(
            r.Id,
            r.SystemId,
            r.Name,
            r.Code,
            r.Description,
            r.SystemTokenTypeId,
            t != null ? t.Code : string.Empty,
            t != null ? t.Name : string.Empty,
            r.CreatedAt,
            r.UpdatedAt,
            r.DeletedAt);

    private ActionResult InvalidSystemIdResult()
    {
        ModelState.AddModelError(nameof(CreateRouteRequest.SystemId), "SystemId inválido ou sistema inativo.");
        return ValidationProblem(ModelState);
    }

    private ActionResult InvalidSystemTokenTypeIdResult()
    {
        ModelState.AddModelError(nameof(CreateRouteRequest.SystemTokenTypeId), "SystemTokenTypeId inválido ou inativo.");
        return ValidationProblem(ModelState);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateRouteRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var systemId = request.SystemId!.Value;
        var systemTokenTypeId = request.SystemTokenTypeId!.Value;

        if (!await SystemExistsAndActiveAsync(systemId))
            return InvalidSystemIdResult();

        if (!await SystemTokenTypeExistsAndActiveAsync(systemTokenTypeId))
            return InvalidSystemTokenTypeIdResult();

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
            SystemTokenTypeId = systemTokenTypeId,
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

        var created = await ProjectRouteResponses(_db.Routes.Where(r => r.Id == entity.Id))
            .FirstAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, created);
    }

    /// <summary>Tamanho de página default quando o cliente não envia <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Limite superior para <c>pageSize</c>; valores acima retornam 400.</summary>
    public const int MaxPageSize = 100;

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? systemId = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool includeDeleted = false)
    {
        if (page <= 0)
            ModelState.AddModelError(nameof(page), "page deve ser maior ou igual a 1.");

        if (pageSize <= 0 || pageSize > MaxPageSize)
            ModelState.AddModelError(nameof(pageSize), $"pageSize deve estar entre 1 e {MaxPageSize}.");

        if (systemId.HasValue && systemId.Value == Guid.Empty)
            ModelState.AddModelError(nameof(systemId), "systemId inválido.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // Quando includeDeleted=true, ignoramos:
        //  - o query filter global (DeletedAt == null), expondo rotas soft-deletadas;
        //  - o filtro "sistema pai ativo" (ActiveRoutesWithActiveSystem),
        //    para que a lista admin enxergue rotas órfãs cujo sistema vinculado também foi soft-deletado.
        IQueryable<AppRoute> query = includeDeleted
            ? _db.Routes.IgnoreQueryFilters()
            : ActiveRoutesWithActiveSystem();

        if (systemId.HasValue)
            query = query.Where(r => r.SystemId == systemId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLikePattern(q.Trim())}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.Code, pattern, "\\") || EF.Functions.ILike(r.Name, pattern, "\\"));
        }

        var total = await query.CountAsync();

        var paged = query
            .OrderBy(r => r.Code)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var data = await ProjectRouteResponses(paged).ToListAsync();

        return Ok(new PagedResponse<RouteResponse>(data, page, pageSize, total));
    }

    /// <summary>
    /// Escapa caracteres curinga (<c>%</c>, <c>_</c>) e o caractere de escape (<c>\</c>) na entrada do usuário
    /// para evitar que sejam interpretados como wildcards no <c>ILIKE</c>. Mantém o termo como busca literal parcial.
    /// </summary>
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.SystemsRoutesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await ProjectRouteResponses(ActiveRoutesWithActiveSystem().Where(r => r.Id == id))
            .FirstOrDefaultAsync();
        if (dto is null)
            return NotFound(new { message = "Route não encontrada." });
        return Ok(dto);
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
        var systemTokenTypeId = request.SystemTokenTypeId!.Value;

        if (!await SystemExistsAndActiveAsync(systemId))
            return InvalidSystemIdResult();

        if (!await SystemTokenTypeExistsAndActiveAsync(systemTokenTypeId))
            return InvalidSystemTokenTypeIdResult();

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
        entity.SystemTokenTypeId = systemTokenTypeId;
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

        var updated = await ProjectRouteResponses(_db.Routes.Where(r => r.Id == entity.Id))
            .FirstAsync();
        return Ok(updated);
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

        if (!await SystemTokenTypeExistsAndActiveAsync(entity.SystemTokenTypeId))
        {
            return BadRequest(new
            {
                message = "Não é possível restaurar a route: o SystemTokenType vinculado está inativo ou foi removido."
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

        /// <summary>
        /// Code do SystemTokenType ("política JWT alvo"). Opcional: quando omitido, usa o code canônico
        /// <c>default</c>. Se informado e desconhecido/inativo, o sync inteiro retorna 400 listando os codes
        /// inválidos (mesmo padrão de PermissionTypeCode).
        /// </summary>
        [MaxLength(50, ErrorMessage = "SystemTokenTypeCode deve ter no máximo 50 caracteres.")]
        public string? SystemTokenTypeCode { get; set; }
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

        // Resolve SystemTokenType: items podem informar SystemTokenTypeCode (opcional). Quando omitido,
        // assume "default". Códigos desconhecidos (ou referenciando registros soft-deletados) retornam 400.
        var explicitTokenCodes = request.Routes
            .Where(r => !string.IsNullOrWhiteSpace(r.SystemTokenTypeCode))
            .Select(r => r.SystemTokenTypeCode!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var requiredTokenCodes = explicitTokenCodes.Contains(SystemTokenTypeSeeder.DefaultCode, StringComparer.Ordinal)
            ? explicitTokenCodes
            : explicitTokenCodes.Concat(new[] { SystemTokenTypeSeeder.DefaultCode }).ToList();

        var tokenIdByCode = await _db.SystemTokenTypes.AsNoTracking()
            .Where(t => requiredTokenCodes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.Ordinal);

        var unknownTokenCodes = explicitTokenCodes
            .Where(c => !tokenIdByCode.ContainsKey(c))
            .ToList();
        if (unknownTokenCodes.Count > 0)
        {
            ModelState.AddModelError(nameof(request.Routes),
                $"SystemTokenTypeCode desconhecido(s): {string.Join(", ", unknownTokenCodes)}.");
            return ValidationProblem(ModelState);
        }

        if (!tokenIdByCode.TryGetValue(SystemTokenTypeSeeder.DefaultCode, out var defaultTokenTypeId))
        {
            ModelState.AddModelError(nameof(request.Routes),
                $"SystemTokenType '{SystemTokenTypeSeeder.DefaultCode}' não encontrado. Execute o SystemTokenTypeSeeder.");
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
            var resolvedTokenTypeId = string.IsNullOrWhiteSpace(item.SystemTokenTypeCode)
                ? defaultTokenTypeId
                : tokenIdByCode[item.SystemTokenTypeCode!];

            Guid routeId;
            if (existingByCode.TryGetValue(item.Code, out var existing))
            {
                var changed = false;
                if (existing.Name != item.Name) { existing.Name = item.Name; changed = true; }
                if (existing.Description != item.Description) { existing.Description = item.Description; changed = true; }
                if (existing.SystemTokenTypeId != resolvedTokenTypeId) { existing.SystemTokenTypeId = resolvedTokenTypeId; changed = true; }
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
                    SystemTokenTypeId = resolvedTokenTypeId,
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
