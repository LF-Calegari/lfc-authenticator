using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(DeletedAt), Name = "IX_RolePermissions_DeletedAt")]
[Index(nameof(RoleId), Name = "IX_RolePermissions_RoleId")]
[Index(nameof(PermissionId), Name = "IX_RolePermissions_PermissionId")]
[Index(nameof(RoleId), nameof(PermissionId), IsUnique = true, Name = "UX_RolePermissions_RoleId_PermissionId")]
[Table("RolePermissions")]
public class AppRolePermission : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RoleId { get; set; }

    [Required]
    public Guid PermissionId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
