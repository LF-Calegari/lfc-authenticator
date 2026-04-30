using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(DeletedAt), Name = "IX_UserRoles_DeletedAt")]
[Index(nameof(UserId), nameof(RoleId), IsUnique = true, Name = "UX_UserRoles_UserId_RoleId")]
[Table("UserRoles")]
public class AppUserRole : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid RoleId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
