using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AuthService.Auth;
using AuthService.Controllers.Common;
using AuthService.Data;
using AuthService.Models;
using AuthService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static AuthService.Helpers.DbExceptionHelper;
using UserEntity = AuthService.Models.User;

namespace AuthService.Controllers.Users;

[ApiController]
[Route("users")]
public partial class UsersController : ControllerBase
{
    private const string UserNotFoundMessage = "Usuário não encontrado.";
    private const string ForceLogoutSelfMessage =
        "Não é possível forçar logout de si mesmo por este endpoint. Utilize GET /auth/logout.";
    private const string InvalidCallerTokenMessage = "Token do chamador inválido.";
    private const int MaxBatchIds = 100;

    private readonly AppDbContext _db;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AppDbContext db, ILogger<UsersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class CreateUserRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email é obrigatório.")]
        [MaxLength(320, ErrorMessage = "Email deve ter no máximo 320 caracteres.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password é obrigatório.")]
        [MaxLength(60, ErrorMessage = "Password deve ter no máximo 60 caracteres.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        public int? Identity { get; set; }

        public Guid? ClientId { get; set; }

        public bool Active { get; set; } = true;
    }

    public class UpdateUserRequest
    {
        [Required(ErrorMessage = "Name é obrigatório.")]
        [MaxLength(80, ErrorMessage = "Name deve ter no máximo 80 caracteres.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email é obrigatório.")]
        [MaxLength(320, ErrorMessage = "Email deve ter no máximo 320 caracteres.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required]
        public int? Identity { get; set; }

        public Guid? ClientId { get; set; }

        [Required]
        public bool? Active { get; set; }
    }

    public class UpdatePasswordRequest
    {
        [Required(ErrorMessage = "Password é obrigatório.")]
        [MaxLength(60, ErrorMessage = "Password deve ter no máximo 60 caracteres.")]
        public string Password { get; set; } = string.Empty;
    }

    public record UserRoleLinkResponse(
        Guid Id,
        Guid UserId,
        Guid RoleId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    public record UserPermissionLinkResponse(
        Guid Id,
        Guid UserId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    public record UserResponse(
        Guid Id,
        string Name,
        string Email,
        Guid? ClientId,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt,
        IReadOnlyList<UserRoleLinkResponse> Roles,
        IReadOnlyList<UserPermissionLinkResponse> Permissions);

    public record UserMinimalResponse(
        Guid Id,
        string Name,
        string Email);

    private static UserResponse ToResponse(
        UserEntity u,
        IReadOnlyList<UserRoleLinkResponse>? roles = null,
        IReadOnlyList<UserPermissionLinkResponse>? permissions = null) =>
        new(
            u.Id,
            u.Name,
            u.Email,
            u.ClientId,
            u.Identity,
            u.Active,
            u.CreatedAt,
            u.UpdatedAt,
            u.DeletedAt,
            roles ?? Array.Empty<UserRoleLinkResponse>(),
            permissions ?? Array.Empty<UserPermissionLinkResponse>());

    private static void ValidateNormalizedUserFields(
        ModelStateDictionary modelState,
        string name,
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(CreateUserRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(email))
            modelState.AddModelError(nameof(CreateUserRequest.Email), "Email é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(password))
            modelState.AddModelError(nameof(CreateUserRequest.Password), "Password é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(CreateUserRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (email.Length > 320)
            modelState.AddModelError(nameof(CreateUserRequest.Email), "Email deve ter no máximo 320 caracteres.");

        if (password.Length > 60)
            modelState.AddModelError(nameof(CreateUserRequest.Password), "Password deve ter no máximo 60 caracteres.");
    }

    private static void ValidateNormalizedUserUpdateFields(ModelStateDictionary modelState, string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
            modelState.AddModelError(nameof(UpdateUserRequest.Name), "Name é obrigatório e não pode ser apenas espaços.");

        if (string.IsNullOrWhiteSpace(email))
            modelState.AddModelError(nameof(UpdateUserRequest.Email), "Email é obrigatório e não pode ser apenas espaços.");

        if (name.Length > 80)
            modelState.AddModelError(nameof(UpdateUserRequest.Name), "Name deve ter no máximo 80 caracteres.");

        if (email.Length > 320)
            modelState.AddModelError(nameof(UpdateUserRequest.Email), "Email deve ter no máximo 320 caracteres.");
    }

    private static ConflictObjectResult EmailConflictResult() =>
        new(new { message = "Já existe um usuário com este Email." });

    private static bool TryParseBatchIds(
        IEnumerable<string> rawIds,
        ModelStateDictionary modelState,
        out List<Guid> ids)
    {
        ids = [];
        var distinct = new HashSet<Guid>();
        var segments = rawIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        if (segments.Length == 0)
        {
            modelState.AddModelError("ids", "Informe pelo menos um id em `ids`.");
            return false;
        }

        if (segments.Length > MaxBatchIds)
        {
            modelState.AddModelError("ids", $"A lista `ids` permite no máximo {MaxBatchIds} itens por requisição.");
            return false;
        }

        foreach (var segment in segments)
        {
            if (!Guid.TryParse(segment, out var parsed))
            {
                modelState.AddModelError("ids", "A lista `ids` deve conter apenas GUIDs válidos.");
                return false;
            }

            if (distinct.Add(parsed))
                ids.Add(parsed);
        }

        return true;
    }

    /// <summary>
    /// Compara por igualdade na coluna (sem função na coluna) para permitir uso do índice único em Email.
    /// O valor persistido já é minúsculo (trim + ToLowerInvariant no create/update).
    /// </summary>
    private static Task<bool> EmailExistsNormalizedAsync(AppDbContext db, string normalizedEmail, Guid? excludeUserId = null)
    {
        var q = db.Users.IgnoreQueryFilters().AsQueryable();
        if (excludeUserId is { } id)
            q = q.Where(u => u.Id != id);
        return q.AnyAsync(u => u.Email == normalizedEmail);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.UsersCreate)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var password = request.Password.Trim();

        ValidateNormalizedUserFields(ModelState, name, email, password);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (await EmailExistsNormalizedAsync(_db, email))
            return EmailConflictResult();

        Guid? clientId = request.ClientId;
        if (clientId.HasValue)
        {
            var existsClient = await _db.Clients.AnyAsync(c => c.Id == clientId.Value);
            if (!existsClient)
                return BadRequest(new { message = "ClientId informado não existe." });
        }
        else
        {
            var usedCpfs = await _db.Clients
                .IgnoreQueryFilters()
                .Where(c => c.Cpf != null)
                .Select(c => c.Cpf!)
                .ToHashSetAsync();
            var generatedClient = LegacyClientFactory.BuildPfClientForUser(
                new UserEntity { Name = name },
                usedCpfs,
                usedCpfs.Count + 1);
            _db.Clients.Add(generatedClient);
            clientId = generatedClient.Id;
        }

        var now = DateTime.UtcNow;
        var identity = request.Identity!.Value;
        var user = new UserEntity
        {
            Name = name,
            Email = email,
            Password = UserPasswordHasher.HashPlainPassword(password),
            ClientId = clientId,
            Identity = identity,
            Active = request.Active,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return EmailConflictResult();
        }

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToResponse(user));
    }

    /// <summary>Tamanho de página default quando o cliente não envia <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Limite superior para <c>pageSize</c>; valores acima retornam 400.</summary>
    public const int MaxPageSize = 100;

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.UsersRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string[]? ids = null,
        [FromQuery] string? q = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool includeDeleted = false)
    {
        if (ids is { Length: > 0 })
        {
            if (!TryParseBatchIds(ids, ModelState, out var parsedIds))
                return ValidationProblem(ModelState);

            var batchUsers = await _db.Users
                .Where(u => parsedIds.Contains(u.Id))
                .Select(u => new UserMinimalResponse(
                    u.Id,
                    u.Name,
                    u.Email))
                .ToListAsync();

            var usersById = batchUsers.ToDictionary(u => u.Id);
            var ordered = parsedIds
                .Where(usersById.ContainsKey)
                .Select(id => usersById[id])
                .ToList();

            return Ok(ordered);
        }

        ValidateGetAllQueryParams(page, pageSize, clientId, active, includeDeleted);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var query = BuildUsersListingQuery(q, clientId, active, includeDeleted);

        // Query 1: COUNT pré-paginação refletindo todos os filtros aplicados.
        var total = await query.CountAsync();

        // Query 2: página corrente, ordenada determinísticamente (CreatedAt DESC com Id como
        // desempate estável, evitando saltos/duplicação entre páginas em ties de timestamp).
        var pageUsers = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        var data = pageUsers.Select(u => ToResponse(u)).ToList();
        return Ok(new PagedResponse<UserResponse>(data, page, pageSize, total));
    }

    /// <summary>
    /// Valida os query params de <see cref="GetAll"/> em modo listagem (sem <c>ids</c>) e
    /// acumula erros em <c>ModelState</c> para resposta unificada via <c>ValidationProblem</c>.
    /// </summary>
    private void ValidateGetAllQueryParams(int page, int pageSize, Guid? clientId, bool? active, bool includeDeleted)
    {
        if (page <= 0)
            ModelState.AddModelError(nameof(page), "page deve ser maior ou igual a 1.");

        if (pageSize <= 0 || pageSize > MaxPageSize)
            ModelState.AddModelError(nameof(pageSize), $"pageSize deve estar entre 1 e {MaxPageSize}.");

        if (clientId.HasValue && clientId.Value == Guid.Empty)
            ModelState.AddModelError(nameof(clientId), "clientId não pode ser Guid.Empty.");

        if (active.HasValue && includeDeleted)
        {
            ModelState.AddModelError(
                nameof(includeDeleted),
                "active e includeDeleted são mutuamente excludentes.");
        }
    }

    /// <summary>
    /// Compõe o <see cref="IQueryable{User}"/> base aplicando a flag de soft-delete (via
    /// <c>IgnoreQueryFilters</c>) e os filtros <c>active</c>, <c>clientId</c> e busca textual <c>q</c>.
    /// </summary>
    /// <remarks>
    /// includeDeleted=true expõe soft-deletados; active=false força <c>IgnoreQueryFilters</c> porque
    /// o query filter global esconderia os registros que ele pretende selecionar. Quando nenhum dos
    /// dois é informado, o filtro global age e a leitura permanece restrita a usuários ativos.
    /// </remarks>
    private IQueryable<UserEntity> BuildUsersListingQuery(string? q, Guid? clientId, bool? active, bool includeDeleted)
    {
        IQueryable<UserEntity> query = (includeDeleted || active == false)
            ? _db.Users.IgnoreQueryFilters()
            : _db.Users;

        if (active.HasValue)
        {
            query = active.Value
                ? query.Where(u => u.DeletedAt == null)
                : query.Where(u => u.DeletedAt != null);
        }

        if (clientId.HasValue)
            query = query.Where(u => u.ClientId == clientId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLikePattern(q.Trim())}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Name, pattern, "\\")
                || EF.Functions.ILike(u.Email, pattern, "\\"));
        }

        return query;
    }

    /// <summary>
    /// Escapa caracteres curinga (<c>%</c>, <c>_</c>) e o caractere de escape (<c>\</c>) na entrada
    /// do usuário para evitar que sejam interpretados como wildcards no <c>ILIKE</c>.
    /// </summary>
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        // Vínculos ativos: link com DeletedAt IS NULL E entidade alvo (Role / Permission) também
        // ativa (sem IgnoreQueryFilters → o query filter global já remove soft-deletados nas
        // entidades alvo). Usamos EXISTS com o DbSet correspondente para evitar trazer a entidade
        // alvo no payload e manter as projeções planas.
        var roles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == id && _db.Roles.Any(r => r.Id == ur.RoleId))
            .OrderBy(ur => ur.CreatedAt)
            .ThenBy(ur => ur.Id)
            .Select(ur => new UserRoleLinkResponse(
                ur.Id,
                ur.UserId,
                ur.RoleId,
                ur.CreatedAt,
                ur.UpdatedAt,
                ur.DeletedAt))
            .ToListAsync();

        var permissions = await _db.UserPermissions.AsNoTracking()
            .Where(up => up.UserId == id && _db.Permissions.Any(p => p.Id == up.PermissionId))
            .OrderBy(up => up.CreatedAt)
            .ThenBy(up => up.Id)
            .Select(up => new UserPermissionLinkResponse(
                up.Id,
                up.UserId,
                up.PermissionId,
                up.CreatedAt,
                up.UpdatedAt,
                up.DeletedAt))
            .ToListAsync();

        return Ok(ToResponse(user, roles, permissions));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateUserRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        ValidateNormalizedUserUpdateFields(ModelState, name, email);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        if (await EmailExistsNormalizedAsync(_db, email, id))
            return new ConflictObjectResult(new { message = "Já existe outro usuário com este Email." });

        if (request.ClientId.HasValue)
        {
            var existsClient = await _db.Clients.AnyAsync(c => c.Id == request.ClientId.Value);
            if (!existsClient)
                return BadRequest(new { message = "ClientId informado não existe." });
        }

        var identity = request.Identity!.Value;
        var active = request.Active!.Value;

        user.Name = name;
        user.Email = email;
        // Não desassocia cliente quando ClientId não é informado no update.
        user.ClientId = request.ClientId ?? user.ClientId;
        user.Identity = identity;
        user.Active = active;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return new ConflictObjectResult(new { message = "Já existe outro usuário com este Email." });
        }

        return Ok(ToResponse(user));
    }

    [HttpPut("{id:guid}/password")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> UpdatePassword(Guid id, [FromBody] UpdatePasswordRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var password = request.Password.Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(nameof(UpdatePasswordRequest.Password),
                "Password é obrigatório e não pode ser apenas espaços.");
            return ValidationProblem(ModelState);
        }

        if (password.Length > 60)
        {
            ModelState.AddModelError(nameof(UpdatePasswordRequest.Password), "Password deve ter no máximo 60 caracteres.");
            return ValidationProblem(ModelState);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        user.Password = UserPasswordHasher.HashPlainPassword(password);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersDelete)]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionPolicies.UsersRestore)]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt != null);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado ou não está deletado." });

        user.DeletedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Usuário restaurado com sucesso." });
    }

    public record ForceLogoutResponse(string Message, Guid UserId, int NewTokenVersion);

    /// <summary>
    /// Invalida todas as sessões ativas do usuário-alvo incrementando o <c>TokenVersion</c>.
    /// Caller deve possuir <c>perm:Users.Update</c>. Self-target retorna 400 (orienta uso de
    /// <c>GET /auth/logout</c>). Usuário inexistente ou soft-deletado retorna 404.
    /// </summary>
    [HttpPost("{id:guid}/force-logout")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> ForceLogout(Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub) || !Guid.TryParse(sub, out var callerId))
            return Unauthorized(new { message = InvalidCallerTokenMessage });

        if (callerId == id)
            return BadRequest(new { message = ForceLogoutSelfMessage });

        // Query filter global já remove soft-deletados (DeletedAt != null) → 404 esperado.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        LogForceLogoutCompleted(user.Id, callerId, user.TokenVersion);
        return Ok(new ForceLogoutResponse(
            "Sessões do usuário invalidadas com sucesso.",
            user.Id,
            user.TokenVersion));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "ForceLogout: target {UserId}, by {CallerId}, newTokenVersion={NewTokenVersion}")]
    private partial void LogForceLogoutCompleted(Guid userId, Guid callerId, int newTokenVersion);

    /// <summary>
    /// Origem de uma permissão na resposta de <see cref="GetEffectivePermissions"/>: <c>direct</c>
    /// (vinculada diretamente ao usuário) ou <c>role</c> (herdada de uma role do usuário).
    /// Os campos <c>RoleId</c>, <c>RoleCode</c> e <c>RoleName</c> são preenchidos somente quando
    /// <c>Kind = "role"</c>.
    /// </summary>
    public record EffectivePermissionSource(
        string Kind,
        Guid? RoleId = null,
        string? RoleCode = null,
        string? RoleName = null);

    public record EffectivePermissionResponse(
        Guid PermissionId,
        string RouteCode,
        string RouteName,
        string PermissionTypeCode,
        string PermissionTypeName,
        Guid SystemId,
        string SystemCode,
        string SystemName,
        IReadOnlyList<EffectivePermissionSource> Sources);

    /// <summary>
    /// Retorna a união consolidada das permissões efetivas do usuário (diretas + via roles), com
    /// a origem agregada por permissão. Apenas vínculos ativos (<c>DeletedAt IS NULL</c> nos links
    /// e nas entidades alvo) compõem o resultado: o filtro global já esconde Permissions, Routes,
    /// Systems, PermissionTypes e Roles soft-deletados; os links UserPermissions/UserRoles/RolePermissions
    /// recebem o predicado <c>DeletedAt == null</c> explícito porque ainda não têm query filter global.
    ///
    /// Filtro opcional <c>?systemId=</c> restringe pelas permissões cuja rota pertence ao sistema.
    /// Ordenação determinística: <c>SystemCode</c>, <c>RouteCode</c>, <c>PermissionTypeCode</c>.
    /// 404 quando o usuário não existe ou está soft-deletado. Política <c>perm:Users.Read</c>.
    /// </summary>
    [HttpGet("{id:guid}/effective-permissions")]
    [Authorize(Policy = PermissionPolicies.UsersRead)]
    public async Task<IActionResult> GetEffectivePermissions(
        Guid id,
        [FromQuery] Guid? systemId = null)
    {
        if (systemId.HasValue && systemId.Value == Guid.Empty)
        {
            ModelState.AddModelError(nameof(systemId), "systemId inválido.");
            return ValidationProblem(ModelState);
        }

        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == id);
        if (!userExists)
            return NotFound(new { message = UserNotFoundMessage });

        // Direct: permissions vinculadas ao usuário (link ativo + permission ativa).
        // O filtro global em Permissions/Routes/Systems/PermissionTypes garante que só ativos
        // são acessíveis sem IgnoreQueryFilters; aqui aplicamos `up.DeletedAt == null` no link.
        var directBase =
            from up in _db.UserPermissions.AsNoTracking()
            where up.UserId == id && up.DeletedAt == null
            join p in _db.Permissions.AsNoTracking() on up.PermissionId equals p.Id
            join r in _db.Routes.AsNoTracking() on p.RouteId equals r.Id
            join s in _db.Systems.AsNoTracking() on r.SystemId equals s.Id
            join t in _db.PermissionTypes.AsNoTracking() on p.PermissionTypeId equals t.Id
            select new
            {
                PermissionId = p.Id,
                RouteCode = r.Code,
                RouteName = r.Name,
                PermissionTypeCode = t.Code,
                PermissionTypeName = t.Name,
                SystemId = s.Id,
                SystemCode = s.Code,
                SystemName = s.Name
            };

        if (systemId.HasValue)
            directBase = directBase.Where(x => x.SystemId == systemId.Value);

        // Role-based: permissions herdadas via UserRoles → RolePermissions. Filtra link ativo
        // tanto em UserRoles quanto em RolePermissions; a Role precisa estar ativa (filter global).
        var roleBase =
            from ur in _db.UserRoles.AsNoTracking()
            where ur.UserId == id && ur.DeletedAt == null
            join role in _db.Roles.AsNoTracking() on ur.RoleId equals role.Id
            join rp in _db.RolePermissions.AsNoTracking() on role.Id equals rp.RoleId
            where rp.DeletedAt == null
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            join r in _db.Routes.AsNoTracking() on p.RouteId equals r.Id
            join s in _db.Systems.AsNoTracking() on r.SystemId equals s.Id
            join t in _db.PermissionTypes.AsNoTracking() on p.PermissionTypeId equals t.Id
            select new
            {
                PermissionId = p.Id,
                RouteCode = r.Code,
                RouteName = r.Name,
                PermissionTypeCode = t.Code,
                PermissionTypeName = t.Name,
                SystemId = s.Id,
                SystemCode = s.Code,
                SystemName = s.Name,
                RoleId = role.Id,
                RoleCode = role.Code,
                RoleName = role.Name
            };

        if (systemId.HasValue)
            roleBase = roleBase.Where(x => x.SystemId == systemId.Value);

        // Materializa as duas faces em paralelo (duas queries independentes, sem UNION SQL para
        // evitar limitações de tradução com tipos heterogêneos). O agrupamento por PermissionId e
        // a consolidação do array `sources` ocorre em memória — N proporcional ao total de linhas
        // efetivas (sem N+1; cada DbSet é tocado no máximo duas vezes em todo o handler).
        var directList = await directBase.ToListAsync();
        var roleList = await roleBase.ToListAsync();

        var rows = new List<EffectivePermissionRow>(directList.Count + roleList.Count);
        foreach (var d in directList)
        {
            rows.Add(new EffectivePermissionRow(
                d.PermissionId,
                d.RouteCode,
                d.RouteName,
                d.PermissionTypeCode,
                d.PermissionTypeName,
                d.SystemId,
                d.SystemCode,
                d.SystemName,
                "direct",
                null,
                null,
                null));
        }
        foreach (var rrow in roleList)
        {
            rows.Add(new EffectivePermissionRow(
                rrow.PermissionId,
                rrow.RouteCode,
                rrow.RouteName,
                rrow.PermissionTypeCode,
                rrow.PermissionTypeName,
                rrow.SystemId,
                rrow.SystemCode,
                rrow.SystemName,
                "role",
                rrow.RoleId,
                rrow.RoleCode,
                rrow.RoleName));
        }

        var result = rows
            .GroupBy(x => x.PermissionId)
            .Select(g =>
            {
                var first = g.First();
                var sources = g
                    .Select(BuildSource)
                    .Distinct(EffectivePermissionSourceComparer.Instance)
                    .OrderBy(s => s.Kind, StringComparer.Ordinal)
                    .ThenBy(s => s.RoleCode, StringComparer.Ordinal)
                    .ToList();
                return new EffectivePermissionResponse(
                    first.PermissionId,
                    first.RouteCode,
                    first.RouteName,
                    first.PermissionTypeCode,
                    first.PermissionTypeName,
                    first.SystemId,
                    first.SystemCode,
                    first.SystemName,
                    sources);
            })
            .OrderBy(p => p.SystemCode, StringComparer.Ordinal)
            .ThenBy(p => p.RouteCode, StringComparer.Ordinal)
            .ThenBy(p => p.PermissionTypeCode, StringComparer.Ordinal)
            .ToList();

        return Ok(result);
    }

    private static EffectivePermissionSource BuildSource(EffectivePermissionRow row) =>
        row.SourceKind == "role"
            ? new EffectivePermissionSource(row.SourceKind, row.RoleId, row.RoleCode, row.RoleName)
            : new EffectivePermissionSource(row.SourceKind);

    /// <summary>
    /// Linha plana intermediária entre a query SQL e o agrupamento por <c>PermissionId</c>. Cada
    /// permissão pode aparecer múltiplas vezes (uma por origem); a consolidação acontece em memória.
    /// </summary>
    private sealed record EffectivePermissionRow(
        Guid PermissionId,
        string RouteCode,
        string RouteName,
        string PermissionTypeCode,
        string PermissionTypeName,
        Guid SystemId,
        string SystemCode,
        string SystemName,
        string SourceKind,
        Guid? RoleId,
        string? RoleCode,
        string? RoleName);

    private sealed class EffectivePermissionSourceComparer : IEqualityComparer<EffectivePermissionSource>
    {
        public static readonly EffectivePermissionSourceComparer Instance = new();

        public bool Equals(EffectivePermissionSource? x, EffectivePermissionSource? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Kind, y.Kind, StringComparison.Ordinal)
                && Nullable.Equals(x.RoleId, y.RoleId);
        }

        public int GetHashCode(EffectivePermissionSource obj) =>
            HashCode.Combine(obj.Kind, obj.RoleId);
    }

    public class AssignPermissionRequest
    {
        [Required(ErrorMessage = "PermissionId é obrigatório.")]
        public Guid? PermissionId { get; set; }
    }

    public class AssignRoleRequest
    {
        [Required(ErrorMessage = "RoleId é obrigatório.")]
        public Guid? RoleId { get; set; }
    }

    public record UserPermissionResponse(
        Guid Id,
        Guid UserId,
        Guid PermissionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    public record UserRoleResponse(
        Guid Id,
        Guid UserId,
        Guid RoleId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt);

    [HttpPost("{userId:guid}/permissions")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> AssignPermission(Guid userId, [FromBody] AssignPermissionRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var permissionId = request.PermissionId!.Value;

        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            return NotFound(new { message = UserNotFoundMessage });

        var permissionExists = await _db.Permissions.AnyAsync(p => p.Id == permissionId);
        if (!permissionExists)
            return BadRequest(new { message = "PermissionId inválido ou permissão inativa." });

        var existing = await _db.UserPermissions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permissionId);

        var utc = DateTime.UtcNow;
        if (existing is not null)
        {
            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.UpdatedAt = utc;
                await _db.SaveChangesAsync();
            }
            return Ok(ToUserPermissionResponse(existing));
        }

        var entity = new AppUserPermission
        {
            UserId = userId,
            PermissionId = permissionId,
            CreatedAt = utc,
            UpdatedAt = utc,
            DeletedAt = null
        };
        _db.UserPermissions.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = userId }, ToUserPermissionResponse(entity));
    }

    [HttpDelete("{userId:guid}/permissions/{permissionId:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> RemovePermission(Guid userId, Guid permissionId)
    {
        var existing = await _db.UserPermissions
            .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permissionId);

        if (existing is null)
            return NotFound(new { message = "Vínculo de permissão não encontrado." });

        var utc = DateTime.UtcNow;
        existing.DeletedAt = utc;
        existing.UpdatedAt = utc;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{userId:guid}/roles")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> AssignRole(Guid userId, [FromBody] AssignRoleRequest request)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var roleId = request.RoleId!.Value;

        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            return NotFound(new { message = UserNotFoundMessage });

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == roleId);
        if (!roleExists)
            return BadRequest(new { message = "RoleId inválido ou role inativa." });

        var existing = await _db.UserRoles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        var utc = DateTime.UtcNow;
        if (existing is not null)
        {
            if (existing.DeletedAt is not null)
            {
                existing.DeletedAt = null;
                existing.UpdatedAt = utc;
                await _db.SaveChangesAsync();
            }
            return Ok(ToUserRoleResponse(existing));
        }

        var entity = new AppUserRole
        {
            UserId = userId,
            RoleId = roleId,
            CreatedAt = utc,
            UpdatedAt = utc,
            DeletedAt = null
        };
        _db.UserRoles.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = userId }, ToUserRoleResponse(entity));
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersUpdate)]
    public async Task<IActionResult> RemoveRole(Guid userId, Guid roleId)
    {
        var existing = await _db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (existing is null)
            return NotFound(new { message = "Vínculo de role não encontrado." });

        var utc = DateTime.UtcNow;
        existing.DeletedAt = utc;
        existing.UpdatedAt = utc;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static UserPermissionResponse ToUserPermissionResponse(AppUserPermission e) =>
        new(e.Id, e.UserId, e.PermissionId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);

    private static UserRoleResponse ToUserRoleResponse(AppUserRole e) =>
        new(e.Id, e.UserId, e.RoleId, e.CreatedAt, e.UpdatedAt, e.DeletedAt);
}
