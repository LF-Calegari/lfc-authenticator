using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(Email), IsUnique = true, Name = "UX_Users_Email")]
[Index(nameof(DeletedAt), Name = "IX_Users_DeletedAt")]
[Table("Users")]
public class User : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(320)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 (ASP.NET Identity). Não armazenar texto plano.</summary>
    [Required]
    [StringLength(500)]
    public string Password { get; set; } = string.Empty;

    public int Identity { get; set; }

    [Required]
    public bool Active { get; set; } = true;

    /// <summary>
    /// Incrementado no logout para invalidar JWTs emitidos anteriormente (claim <c>tv</c>).
    /// </summary>
    [Required]
    public int TokenVersion { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }
}