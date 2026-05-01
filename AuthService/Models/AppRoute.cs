using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(Code), IsUnique = true, Name = "UX_Routes_Code")]
[Index(nameof(SystemId), nameof(Code), IsUnique = true, Name = "UX_Routes_SystemId_Code")]
[Index(nameof(SystemTokenTypeId), Name = "IX_Routes_SystemTokenTypeId")]
[Index(nameof(DeletedAt), Name = "IX_Routes_DeletedAt")]
[Table("Routes")]
public class AppRoute : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SystemId { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public Guid SystemTokenTypeId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
