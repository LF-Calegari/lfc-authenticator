using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

[Index(nameof(Cpf), IsUnique = true, Name = "UX_Clients_Cpf")]
[Index(nameof(Cnpj), IsUnique = true, Name = "UX_Clients_Cnpj")]
[Index(nameof(DeletedAt), Name = "IX_Clients_DeletedAt")]
[Table("Clients")]
public class Client : ISoftDelete
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(2)]
    public string Type { get; set; } = string.Empty; // PF | PJ

    [StringLength(11)]
    public string? Cpf { get; set; }

    [StringLength(140)]
    public string? FullName { get; set; }

    [StringLength(14)]
    public string? Cnpj { get; set; }

    [StringLength(180)]
    public string? CorporateName { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }
}
