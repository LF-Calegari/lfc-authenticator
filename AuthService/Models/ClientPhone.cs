using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(ClientId), nameof(Type), nameof(Number), IsUnique = true, Name = "UX_ClientPhones_ClientId_Type_Number")]
[Table("ClientPhones")]
public class ClientPhone
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [StringLength(12)]
    public string Type { get; set; } = string.Empty; // mobile | phone

    [Required]
    [StringLength(20)]
    public string Number { get; set; } = string.Empty;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
