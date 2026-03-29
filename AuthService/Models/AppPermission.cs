using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(DeletedAt), Name = "IX_Permissions_DeletedAt")]
[Index(nameof(SystemId), Name = "IX_Permissions_SystemId")]
[Index(nameof(PermissionTypeId), Name = "IX_Permissions_PermissionTypeId")]
[Table("Permissions")]
public class AppPermission : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SystemId { get; set; }

    [Required]
    public Guid PermissionTypeId { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
