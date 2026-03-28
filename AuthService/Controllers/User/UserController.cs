using AuthService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserEntity = AuthService.Models.User;

namespace AuthService.Controllers.Users;

[ApiController]
[Route("users")]
public class UserController : ControllerBase
{
    private readonly AppDbContext _db;

    public UserController(AppDbContext db)
    {
        _db = db;
    }

    public record CreateUserRequest(string Name, string Email, string Password, int Identity, bool Active = true);
    public record UpdateUserRequest(string Name, string Email, string Password, int Identity, bool Active = true);

    public record UserResponse(
        Guid Id,
        string Name,
        string Email,
        int Identity,
        bool Active,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt
    );

    private static UserResponse ToResponse(UserEntity u) =>
        new(u.Id, u.Name, u.Email, u.Identity, u.Active, u.CreatedAt, u.UpdatedAt, u.DeletedAt);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users
            .OrderBy(u => u.CreatedAt)
            .Select(u => new UserResponse(
                u.Id,
                u.Name,
                u.Email,
                u.Identity,
                u.Active,
                u.CreatedAt,
                u.UpdatedAt,
                u.DeletedAt))
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var now = DateTime.UtcNow;
        var exists = await _db.Users.AnyAsync(u => u.Email == request.Email);

        if (exists)
            return Conflict(new { message = "Email já em uso." });

        var user = new UserEntity
        {
            Name = request.Name,
            Email = request.Email,
            Password = request.Password,
            Identity = request.Identity,
            Active = request.Active,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToResponse(user));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User não encontrado." });
        return Ok(ToResponse(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateById(Guid id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User não encontrado." });

        var emailInUse = await _db.Users.AnyAsync(u => u.Id != id && u.Email == request.Email);
        if (emailInUse)
            return Conflict(new { message = "Email já em uso por outro usuário." });

        user.Name = request.Name;
        user.Email = request.Email;
        user.Password = request.Password;
        user.Identity = request.Identity;
        user.Active = request.Active;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteById(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User não encontrado." });

        user.DeletedAt = DateTime.UtcNow;
        user.Active = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreById(Guid id)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt != null);

        if (user is null) return NotFound(new { message = "User não encontrado (ou não está deletado)." });

        user.DeletedAt = null;
        user.Active = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }
}
