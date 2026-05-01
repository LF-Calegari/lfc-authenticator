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
        string RouteCode,
        string RouteName,
        Guid SystemId,
        string SystemCode,
        string SystemName,
        Guid PermissionTypeId,
        string PermissionTypeCode,
        string PermissionTypeName,
        string? Description,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    /// <summary>
    /// Projeção compartilhada por <see cref="GetById"/>, <see cref="Create"/> e <see cref="UpdateById"/>.
    /// Denormaliza <c>Route</c>, <c>System</c> (via <c>Route.SystemId</c>) e <c>PermissionType</c> em
    /// uma única query com Joins (sem N+1). Não aplica <c>IgnoreQueryFilters()</c> nos DbSets
    /// aninhados — o filtro global de soft-delete já garante que rotas/sistemas/tipos soft-deletados
    /// fiquem fora dos caminhos sem <c>includeDeleted</c>. Left Join devolve string vazia /
    /// <see cref="Guid.Empty"/> nos campos quando o lado direito não casa. <see cref="GetAll"/>
    /// usa <see cref="OrderProjectAndPaginate"/>, que aplica a ordenação determinística sobre as
    /// colunas nativas dos Joins (necessário para o EF traduzir o ORDER BY em SQL).
    /// </summary>
    private IQueryable<PermissionResponse> ProjectPermissionResponses(IQueryable<AppPermission> source) =>
        from p in source
        join r in _db.Routes on p.RouteId equals r.Id into rg
        from r in rg.DefaultIfEmpty()
        join s in _db.Systems on (r != null ? r.SystemId : Guid.Empty) equals s.Id into sg
        from s in sg.DefaultIfEmpty()
        join t in _db.PermissionTypes on p.PermissionTypeId equals t.Id into tg
        from t in tg.DefaultIfEmpty()
        select new PermissionResponse(
            p.Id,
            p.RouteId,
            r != null ? r.Code : string.Empty,
            r != null ? r.Name : string.Empty,
            r != null ? r.SystemId : Guid.Empty,
            s != null ? s.Code : string.Empty,
            s != null ? s.Name : string.Empty,
            p.PermissionTypeId,
            t != null ? t.Code : string.Empty,
            t != null ? t.Name : string.Empty,
            p.Description,
            p.CreatedAt,
            p.UpdatedAt,
            p.DeletedAt);

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
        var created = await ProjectPermissionResponses(_db.Permissions.Where(p => p.Id == entity.Id))
            .FirstAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, created);
    }

    /// <summary>Tamanho de página default quando o cliente não envia <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Limite superior para <c>pageSize</c>; valores acima retornam 400.</summary>
    public const int MaxPageSize = 100;

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.PermissionsRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? systemId = null,
        [FromQuery] Guid? routeId = null,
        [FromQuery] Guid? permissionTypeId = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool includeDeleted = false)
    {
        PagingQueryHelper.ValidatePaging(ModelState, page, pageSize, MaxPageSize);
        PagingQueryHelper.ValidateOptionalSystemId(ModelState, systemId);

        if (routeId.HasValue && routeId.Value == Guid.Empty)
            ModelState.AddModelError(nameof(routeId), "routeId inválido.");

        if (permissionTypeId.HasValue && permissionTypeId.Value == Guid.Empty)
            ModelState.AddModelError(nameof(permissionTypeId), "permissionTypeId inválido.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // Quando includeDeleted=true, listamos tudo: permissões soft-deletadas e permissões cuja
        // rota/tipo foram soft-deletados. Caso contrário, usamos o filtro padrão que exige rota
        // e tipo ativos (mesmo critério de leitura aplicado pelas demais leituras do controller).
        IQueryable<AppPermission> query = includeDeleted
            ? _db.Permissions.IgnoreQueryFilters()
            : ActivePermissionsWithActiveParents();

        if (systemId.HasValue)
        {
            // Filtra via Routes.SystemId. Quando includeDeleted=true precisamos enxergar permissões
            // cuja rota foi soft-deletada — daí o IgnoreQueryFilters() na subquery; quando false, o
            // filtro global já garante que apenas rotas ativas casem.
            var sid = systemId.Value;
            query = includeDeleted
                ? query.Where(p => _db.Routes.IgnoreQueryFilters().Any(r => r.Id == p.RouteId && r.SystemId == sid))
                : query.Where(p => _db.Routes.Any(r => r.Id == p.RouteId && r.SystemId == sid));
        }

        if (routeId.HasValue)
            query = query.Where(p => p.RouteId == routeId.Value);

        if (permissionTypeId.HasValue)
            query = query.Where(p => p.PermissionTypeId == permissionTypeId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{PagingQueryHelper.EscapeLikePattern(q.Trim())}%";
            // ILIKE em RouteCode, RouteName e Description. Usamos a mesma fonte de Routes que a
            // projeção (com/sem IgnoreQueryFilters() conforme includeDeleted) para que a busca
            // case com os campos exibidos.
            var routesSource = includeDeleted ? _db.Routes.IgnoreQueryFilters() : _db.Routes;
            query = query.Where(p =>
                routesSource.Any(r => r.Id == p.RouteId
                    && (EF.Functions.ILike(r.Code, pattern, "\\") || EF.Functions.ILike(r.Name, pattern, "\\")))
                || (p.Description != null && EF.Functions.ILike(p.Description, pattern, "\\")));
        }

        var total = await query.CountAsync();

        // Ordenação determinística cruzando Routes/Systems/PermissionTypes: aplicamos sobre a
        // projeção plana (com colunas nativas das tabelas Joinadas) para que o EF traduza o
        // ORDER BY direto em SQL — usar colunas do `record` PermissionResponse no OrderBy faz
        // o EF abrir mão da tradução. Em seguida, materializamos cada linha no record final.
        var data = await OrderProjectAndPaginate(query, page, pageSize, includeDeleted)
            .ToListAsync();

        return Ok(new PagedResponse<PermissionResponse>(data, page, pageSize, total));
    }

    /// <summary>
    /// Aplica a projeção plana (colunas nativas dos Joins) com a ordenação determinística e
    /// <c>Skip</c>/<c>Take</c> traduzidos em SQL. A materialização final no <see cref="PermissionResponse"/>
    /// é feita após o ORDER BY/OFFSET para manter a query 100% server-side e sem N+1.
    /// </summary>
    private IQueryable<PermissionResponse> OrderProjectAndPaginate(
        IQueryable<AppPermission> source, int page, int pageSize, bool includeDeleted)
    {
        var routes = includeDeleted ? _db.Routes.IgnoreQueryFilters() : _db.Routes;
        var systems = includeDeleted ? _db.Systems.IgnoreQueryFilters() : _db.Systems;
        var types = includeDeleted ? _db.PermissionTypes.IgnoreQueryFilters() : _db.PermissionTypes;

        var flat =
            from p in source
            join r in routes on p.RouteId equals r.Id into rg
            from r in rg.DefaultIfEmpty()
            join s in systems on (r != null ? r.SystemId : Guid.Empty) equals s.Id into sg
            from s in sg.DefaultIfEmpty()
            join t in types on p.PermissionTypeId equals t.Id into tg
            from t in tg.DefaultIfEmpty()
            select new
            {
                Permission = p,
                RouteCode = r != null ? r.Code : string.Empty,
                RouteName = r != null ? r.Name : string.Empty,
                SystemId = r != null ? r.SystemId : Guid.Empty,
                SystemCode = s != null ? s.Code : string.Empty,
                SystemName = s != null ? s.Name : string.Empty,
                PermissionTypeCode = t != null ? t.Code : string.Empty,
                PermissionTypeName = t != null ? t.Name : string.Empty
            };

        return flat
            .OrderBy(x => x.SystemCode)
            .ThenBy(x => x.RouteCode)
            .ThenBy(x => x.PermissionTypeCode)
            .ThenBy(x => x.Permission.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PermissionResponse(
                x.Permission.Id,
                x.Permission.RouteId,
                x.RouteCode,
                x.RouteName,
                x.SystemId,
                x.SystemCode,
                x.SystemName,
                x.Permission.PermissionTypeId,
                x.PermissionTypeCode,
                x.PermissionTypeName,
                x.Permission.Description,
                x.Permission.CreatedAt,
                x.Permission.UpdatedAt,
                x.Permission.DeletedAt));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.PermissionsRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await ProjectPermissionResponses(ActivePermissionsWithActiveParents().Where(p => p.Id == id))
            .FirstOrDefaultAsync();
        if (dto is null)
            return NotFound(new { message = "Permissão não encontrada." });
        return Ok(dto);
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
        var updated = await ProjectPermissionResponses(_db.Permissions.Where(p => p.Id == entity.Id))
            .FirstAsync();
        return Ok(updated);
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
