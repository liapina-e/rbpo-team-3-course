using DefaultNamespace;
using Microsoft.EntityFrameworkCore;

namespace TempShare.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<Permission> Permissions => Set<Permission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();

        b.Entity<Permission>().HasIndex(p => new { p.FileId, p.RecipientId }).IsUnique();

        b.Entity<Permission>()
            .HasOne(p => p.File)
            .WithMany(f => f.Permissions)
            .HasForeignKey(p => p.FileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}