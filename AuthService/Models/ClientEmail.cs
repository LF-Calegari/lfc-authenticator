using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(ClientId), nameof(Email), IsUnique = true, Name = "UX_ClientEmails_ClientId_Email")]
[Table("ClientEmails")]
public class ClientEmail
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [StringLength(320)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
