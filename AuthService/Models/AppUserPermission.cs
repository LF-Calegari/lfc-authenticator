using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(DeletedAt), Name = "IX_UserPermissions_DeletedAt")]
[Index(nameof(UserId), Name = "IX_UserPermissions_UserId")]
[Index(nameof(PermissionId), Name = "IX_UserPermissions_PermissionId")]
[Index(nameof(UserId), nameof(PermissionId), IsUnique = true, Name = "UX_UserPermissions_UserId_PermissionId")]
[Table("UserPermissions")]
public class AppUserPermission : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid PermissionId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
