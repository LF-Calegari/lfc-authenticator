using System.ComponentModel.DataAnnotations;
using AuthService.Auth;
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
public class UsersController : ControllerBase
{
    private const string UserNotFoundMessage = "Usuário não encontrado.";
    private const int MaxBatchIds = 100;

    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
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
        int Id,
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

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.UsersRead)]
    public async Task<IActionResult> GetAll([FromQuery] string[]? ids = null)
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

        var users = await _db.Users
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();
        return Ok(users.Select(u => ToResponse(u)).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.UsersRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = UserNotFoundMessage });

        return Ok(new UserMinimalResponse(user.Id, user.Name, user.Email));
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
}
