namespace AuthService.Models;

/// <summary>
/// Marca entidades com soft delete. O AppDbContext aplica filtro global DeletedAt == null.
/// </summary>
public interface ISoftDelete
{
    DateTime? DeletedAt { get; set; }
}
