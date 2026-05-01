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

    /// <summary>Payload base com os campos compartilhados entre Create e Update de roles.</summary>
    public abstract class RoleRequestBase
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

    public sealed class CreateRoleRequest : RoleRequestBase { }

    public sealed class UpdateRoleRequest : RoleRequestBase { }

    public record RoleResponse(
        Guid Id,
        Guid SystemId,
        string Name,
        string Code,
        string? Description,
        int PermissionsCount,
        int UsersCount,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    /// <summary>
    /// Projeção compartilhada por <see cref="GetAll"/>, <see cref="GetById"/>, <see cref="Create"/>
    /// e <see cref="UpdateById"/>. Materializa <c>PermissionsCount</c> e <c>UsersCount</c> como
    /// subselects EF Core (traduzidos para subqueries SQL) — evita N+1 sem depender de propriedades
    /// de navegação (o modelo não declara coleções) e respeita o contrato: contagens consideram
    /// apenas vínculos ativos (<c>DeletedAt IS NULL</c>) cuja entidade-alvo (Permission/User) ainda
    /// esteja ativa, mesmo quando a Role retornada está soft-deletada (<c>includeDeleted=true</c>).
    /// Mantemos os subselects sem <c>IgnoreQueryFilters()</c> nos DbSets aninhados — o filtro
    /// global <c>DeletedAt IS NULL</c> já garante que vínculos e entidades alvo soft-deletadas
    /// fiquem fora da contagem; aplicar <c>IgnoreQueryFilters()</c> aqui propagaria o "ignore"
    /// para a raiz e vazaria roles soft-deletadas no caminho default.
    /// </summary>
    private IQueryable<RoleResponse> ProjectRoleResponses(IQueryable<AppRole> source) =>
        source.Select(r => new RoleResponse(
            r.Id,
            r.SystemId,
            r.Name,
            r.Code,
            r.Description,
            _db.RolePermissions.Count(rp =>
                rp.RoleId == r.Id
                && _db.Permissions.Any(p => p.Id == rp.PermissionId)),
            _db.UserRoles.Count(ur =>
                ur.RoleId == r.Id
                && _db.Users.Any(u => u.Id == ur.UserId)),
            r.CreatedAt,
            r.UpdatedAt,
            r.DeletedAt));

    private const string RoleNotFoundMessage = "Role não encontrado.";

    private static ConflictObjectResult UniqueConflictResult() =>
        new(new { message = "Já existe um role com este Code neste sistema." });

    private static NotFoundObjectResult RoleNotFoundResult() =>
        new(new { message = RoleNotFoundMessage });

    private async Task<bool> SystemExistsAndActiveAsync(Guid systemId) =>
        systemId != Guid.Empty && await _db.Systems.AnyAsync(s => s.Id == systemId);

    private ActionResult InvalidSystemIdResult()
    {
        ModelState.AddModelError(nameof(CreateRoleRequest.SystemId), "SystemId inválido ou sistema inativo.");
        return ValidationProblem(ModelState);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.RolesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var systemId = request.SystemId!.Value;

        if (!await SystemExistsAndActiveAsync(systemId))
            return InvalidSystemIdResult();

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        PagingQueryHelper.ValidateNameCodeDescription(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.SystemId == systemId && r.Code == code))
        {
            _logger.LogWarning("Conflito ao criar role: já existe Code {Code} no sistema {SystemId}.", code, systemId);
            return UniqueConflictResult();
        }

        var now = DateTime.UtcNow;
        var entity = new AppRole
        {
            SystemId = systemId,
            Name = name,
            Code = code,
            Description = description,
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
            _logger.LogWarning(ex, "Conflito de unicidade ao criar role com Code {Code} no sistema {SystemId}.", code, systemId);
            return UniqueConflictResult();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return InvalidSystemIdResult();
        }

        LogRoleCreated(entity.Id, code);
        var created = await ProjectRoleResponses(_db.Roles.Where(r => r.Id == entity.Id))
            .FirstAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, created);
    }

    /// <summary>Tamanho de página default quando o cliente não envia <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Limite superior para <c>pageSize</c>; valores acima retornam 400.</summary>
    public const int MaxPageSize = 100;

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.RolesRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? systemId = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool includeDeleted = false)
    {
        PagingQueryHelper.ValidatePaging(ModelState, page, pageSize, MaxPageSize);
        PagingQueryHelper.ValidateOptionalSystemId(ModelState, systemId);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        IQueryable<AppRole> query = includeDeleted
            ? _db.Roles.IgnoreQueryFilters()
            : _db.Roles;

        if (systemId.HasValue)
            query = query.Where(r => r.SystemId == systemId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{PagingQueryHelper.EscapeLikePattern(q.Trim())}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.Code, pattern, "\\") || EF.Functions.ILike(r.Name, pattern, "\\"));
        }

        var total = await query.CountAsync();

        var paged = query
            .OrderBy(r => r.Code)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var data = await ProjectRoleResponses(paged).ToListAsync();

        return Ok(new PagedResponse<RoleResponse>(data, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dto = await ProjectRoleResponses(_db.Roles.Where(r => r.Id == id))
            .FirstOrDefaultAsync();
        if (dto is null)
            return RoleNotFoundResult();
        return Ok(dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return RoleNotFoundResult();

        var systemId = request.SystemId!.Value;

        // SystemId é imutável após criação. Tentativa de mudar retorna 400.
        if (systemId != entity.SystemId)
        {
            ModelState.AddModelError(nameof(UpdateRoleRequest.SystemId),
                "SystemId é imutável após a criação do role.");
            return ValidationProblem(ModelState);
        }

        var name = request.Name.Trim();
        var code = request.Code.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        PagingQueryHelper.ValidateNameCodeDescription(ModelState, name, code, description);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await _db.Roles.IgnoreQueryFilters()
            .AnyAsync(r => r.Id != id && r.SystemId == entity.SystemId && r.Code == code))
        {
            _logger.LogWarning("Conflito ao atualizar role {RoleId}: Code {Code} já em uso no sistema {SystemId}.",
                id, code, entity.SystemId);
            return new ConflictObjectResult(new { message = "Já existe outro role com este Code neste sistema." });
        }

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
            _logger.LogWarning(ex, "Conflito de unicidade ao atualizar role {RoleId} para Code {Code}.", id, code);
            return new ConflictObjectResult(new { message = "Já existe outro role com este Code neste sistema." });
        }

        LogRoleUpdated(id);
        var updated = await ProjectRoleResponses(_db.Roles.Where(r => r.Id == entity.Id))
            .FirstAsync();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null)
            return RoleNotFoundResult();

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

    public class AssignPermissionRequest
    {
        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid? PermissionId { get; set; }
    }

    public record RolePermissionResponse(
        Guid Id,
        Guid RoleId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    [HttpPost("{roleId:guid}/permissions")]
    [Authorize(Policy = PermissionPolicies.RolesUpdate)]
    public async Task<IActionResult> AssignPermission(Guid roleId, [FromBody] AssignPermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var permissionId = request.PermissionId!.Value;

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == roleId);
        if (!roleExists)
            return RoleNotFoundResult();

        var permissionExists = await _db.Permissions.AnyAsync(p => p.Id == permissionId);
        if (!permissionExists)
            return BadRequest(new { message = "PermissionId inválido ou permissão inativa." });

        var existing = await _db.RolePermissions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        var utc = DateTime.UtcNow;
        if (existing is not null)
        {
            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.UpdatedAt = utc;
                await _db.SaveChangesAsync();
            }
            return Ok(ToRolePermissionResponse(existing));
        }

        var entity = new AppRolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId,
            CreatedAt = utc,
            UpdatedAt = utc,
            DeletedAt = null
        };
        _db.RolePermissions.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = roleId }, ToRolePermissionResponse(entity));
    }

    [HttpDelete("{roleId:guid}/permissions/{permissionId:guid}")]
    [Authorize(Policy = PermissionPolicies.RolesUpdate)]
    public async Task<IActionResult> RemovePermission(Guid roleId, Guid permissionId)
    {
        var existing = await _db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        if (existing is null)
            return NotFound(new { message = "Vínculo de permissão não encontrado." });

        var utc = DateTime.UtcNow;
        existing.DeletedAt = utc;
        existing.UpdatedAt = utc;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static RolePermissionResponse ToRolePermissionResponse(AppRolePermission e) =>
        new(e.Id, e.RoleId, e.PermissionId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role criado: {RoleId}, Code {Code}.")]
    private partial void LogRoleCreated(Guid roleId, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role atualizado: {RoleId}.")]
    private partial void LogRoleUpdated(Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role excluído (soft): {RoleId}.")]
    private partial void LogRoleDeleted(Guid roleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role restaurado: {RoleId}.")]
    private partial void LogRoleRestored(Guid roleId);
}
