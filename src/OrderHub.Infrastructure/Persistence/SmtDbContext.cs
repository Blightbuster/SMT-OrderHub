using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OrderHub.Domain;

namespace OrderHub.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the SMT OrderHub application.
/// Maps the Order / Board / Component aggregate with explicit many-to-many
/// join entities, optimistic concurrency (RowVersion) tokens, and the
/// ASP.NET Core Identity user store.
/// </summary>
public class SmtDbContext : IdentityDbContext
{
    public SmtDbContext(DbContextOptions<SmtDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<OrderBoard> OrderBoards => Set<OrderBoard>();
    public DbSet<BoardComponent> BoardComponents => Set<BoardComponent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Order ----
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Name).IsRequired().HasMaxLength(200);
            // Case-insensitive unique index on Name for search/uniqueness.
            entity.HasIndex(o => o.Name).IsUnique();
            entity.Property(o => o.OrderDate).IsRequired();
            entity.Property(o => o.RowVersion).IsConcurrencyToken();
        });

        // ---- Board ----
        modelBuilder.Entity<Board>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(b => b.Name).IsUnique();
            entity.Property(b => b.Length).IsRequired();
            entity.Property(b => b.Width).IsRequired();
            entity.Property(b => b.RowVersion).IsConcurrencyToken();
        });

        // ---- Component ----
        modelBuilder.Entity<Component>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(c => c.Name).IsUnique();
            entity.Property(c => c.Quantity).IsRequired();
            entity.Property(c => c.RowVersion).IsConcurrencyToken();
        });

        // ---- OrderBoard (explicit join entity with extended attribute) ----
        modelBuilder.Entity<OrderBoard>(entity =>
        {
            entity.HasKey(ob => new { ob.OrderId, ob.BoardId });

            entity.HasOne(ob => ob.Order)
                  .WithMany(o => o.OrderBoards)
                  .HasForeignKey(ob => ob.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ob => ob.Board)
                  .WithMany(b => b.OrderBoards)
                  .HasForeignKey(ob => ob.BoardId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(ob => ob.BoardQuantity).IsRequired();
            entity.Property(ob => ob.RowVersion).IsConcurrencyToken();
        });

        // ---- BoardComponent (explicit join entity with extended attribute) ----
        modelBuilder.Entity<BoardComponent>(entity =>
        {
            entity.HasKey(bc => new { bc.BoardId, bc.ComponentId });

            entity.HasOne(bc => bc.Board)
                  .WithMany(b => b.BoardComponents)
                  .HasForeignKey(bc => bc.BoardId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(bc => bc.Component)
                  .WithMany(c => c.BoardComponents)
                  .HasForeignKey(bc => bc.ComponentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(bc => bc.PlacementCount).IsRequired();
            entity.Property(bc => bc.RowVersion).IsConcurrencyToken();
        });
    }

    /// <summary>
    /// Centrally regenerates the optimistic concurrency token on every modified
    /// entity, so write paths never need to bump RowVersion manually.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpRowVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        BumpRowVersions();
        return base.SaveChanges();
    }

    private void BumpRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IHasRowVersion>()
                     .Where(e => e.State is EntityState.Modified))
        {
            entry.Entity.RowVersion = Guid.NewGuid();
        }
    }
}
