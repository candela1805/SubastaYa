using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Models;

namespace SubastaYa.API.Data;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Subasta> Subastas => Set<Subasta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Subasta>(entity =>
        {
            entity.ToTable("Subastas");

            entity.HasKey(subasta => subasta.Id);

            entity.Property(subasta => subasta.Titulo)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(subasta => subasta.Descripcion)
                .HasMaxLength(2_000);

            entity.Property(subasta => subasta.PrecioInicial)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.PrecioActual)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(subasta => subasta.FechaFinUtc);
            entity.HasIndex(subasta => subasta.Activa);
        });
    }
}