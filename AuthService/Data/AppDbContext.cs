using System.Linq.Expressions;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientEmail> ClientEmails => Set<ClientEmail>();
    public DbSet<ClientPhone> ClientPhones => Set<ClientPhone>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSystem> Systems => Set<AppSystem>();
    public DbSet<AppRoute> Routes => Set<AppRoute>();
    public DbSet<AppPermissionType> PermissionTypes => Set<AppPermissionType>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AppPermission> Permissions => Set<AppPermission>();
    public DbSet<AppSystemTokenType> SystemTokenTypes => Set<AppSystemTokenType>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();
    public DbSet<AppUserPermission> UserPermissions => Set<AppUserPermission>();
    public DbSet<AppRolePermission> RolePermissions => Set<AppRolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppRoute>()
            .HasOne<AppSystem>()
            .WithMany()
            .HasForeignKey(r => r.SystemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasIndex(c => c.Cpf)
                .IsUnique()
                .HasDatabaseName("UX_Clients_Cpf")
                .HasFilter("\"Cpf\" IS NOT NULL");

            entity.HasIndex(c => c.Cnpj)
                .IsUnique()
                .HasDatabaseName("UX_Clients_Cnpj")
                .HasFilter("\"Cnpj\" IS NOT NULL");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(u => u.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClientEmail>(entity =>
        {
            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClientPhone>(entity =>
        {
            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppPermission>(entity =>
        {
            entity.HasOne<AppSystem>()
                .WithMany()
                .HasForeignKey(p => p.SystemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppPermissionType>()
                .WithMany()
                .HasForeignKey(p => p.PermissionTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppRole>()
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppUserPermission>(entity =>
        {
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppPermission>()
                .WithMany()
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppRolePermission>(entity =>
        {
            entity.HasOne<AppRole>()
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppPermission>()
                .WithMany()
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var clrType in modelBuilder.Model.GetEntityTypes()
                     .Where(et => typeof(ISoftDelete).IsAssignableFrom(et.ClrType))
                     .Select(et => et.ClrType))
        {
            var parameter = Expression.Parameter(clrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDelete.DeletedAt));
            var nullConstant = Expression.Constant(null, typeof(DateTime?));
            var body = Expression.Equal(property, nullConstant);
            var lambda = Expression.Lambda(body, parameter);
            modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        }
    }
}
