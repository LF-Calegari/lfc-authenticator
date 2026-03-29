using System.Linq.Expressions;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSystem> Systems => Set<AppSystem>();
    public DbSet<AppRoute> Routes => Set<AppRoute>();
    public DbSet<AppPermissionType> PermissionTypes => Set<AppPermissionType>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AppPermission> Permissions => Set<AppPermission>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppRoute>()
            .HasOne<AppSystem>()
            .WithMany()
            .HasForeignKey(r => r.SystemId)
            .OnDelete(DeleteBehavior.Restrict);

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

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(ISoftDelete.DeletedAt));
                var nullConstant = Expression.Constant(null, typeof(DateTime?));
                var body = Expression.Equal(property, nullConstant);
                var lambda = Expression.Lambda(body, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }
}