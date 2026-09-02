using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Models;
using System;

namespace SubastaYa.API.Data;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Subasta> Subastas => Set<Subasta>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Billetera> Billeteras => Set<Billetera>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Puja> Pujas => Set<Puja>();
    public DbSet<TransaccionLedger> TransaccionesLedger => Set<TransaccionLedger>();
    public DbSet<AuditoriaLog> AuditoriaLogs => Set<AuditoriaLog>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateConcurrencyVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        UpdateConcurrencyVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void UpdateConcurrencyVersions()
    {
        var entries = ChangeTracker.Entries()
            .Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified &&
                entry.Entity is Subasta or Billetera);

        foreach (var entry in entries)
        {
            entry.Property(nameof(Subasta.Version)).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuracion Fluent Api Para la subasta
        modelBuilder.Entity<Subasta>(entity =>
        {
            entity.ToTable("Subastas");
            entity.HasKey(subasta => subasta.Id);

            entity.Property(subasta => subasta.Titulo)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(subasta => subasta.Descripcion)
                .HasMaxLength(2_000);

            entity.Property(subasta => subasta.PrecioBase)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.IncrementoMinimo)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.Estado)
                .IsRequired()
                .HasMaxLength(20);
            
            // Control de concurrencia optimista
            entity.Property(subasta => subasta.Version)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            
            
            entity.HasIndex(subasta => subasta.FechaFin);
            entity.HasIndex(subasta => subasta.Estado);
        });

        // Configuracion Fluent Api para billetera
        modelBuilder.Entity<Billetera>(entity =>
        {
            entity.Property(b => b.SaldoTotal).HasPrecision(18, 2);
            entity.Property(b => b.SaldoRetenido).HasPrecision(18,2);
            entity.Property(b => b.Version)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
        });
        
        modelBuilder.Entity<Puja>().Property(p => p.Monto).HasPrecision(18, 2);
        modelBuilder.Entity<TransaccionLedger>().Property(t => t.Monto).HasPrecision(18, 2);

        // Seed data determinista para evitar cambios de modelo en cada migración.
        var seedDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var activeStart = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var activeEnd = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var scheduledStart = new DateTime(2031, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var scheduledEnd = new DateTime(2031, 1, 2, 12, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Usuario>().HasData(
            new Usuario { Id = 1, Email = "vendedor@test.com", Nombre = "Vendedor", PasswordHash = "hash123", FechaRegistro = seedDate },
            new Usuario { Id = 2, Email = "comprador1@test.com", Nombre = "Comprador 1", PasswordHash = "hash123", FechaRegistro = seedDate },
            new Usuario { Id = 3, Email = "comprador2@test.com", Nombre = "Comprador 2", PasswordHash = "hash123", FechaRegistro = seedDate },
            new Usuario { Id = 4, Email = "sinfondos@test.com", Nombre = "Sin Fondos", PasswordHash = "hash123", FechaRegistro = seedDate }
        );
        modelBuilder.Entity<Billetera>().HasData(
            new Billetera { Id = 1, UsuarioId = 1, SaldoTotal = 0, SaldoRetenido = 0, Version = new byte[] { 1 } },
            new Billetera { Id = 2, UsuarioId = 2, SaldoTotal = 150000, SaldoRetenido = 45000, Version = new byte[] { 2 } },
            new Billetera { Id = 3, UsuarioId = 3, SaldoTotal = 200000, SaldoRetenido = 0, Version = new byte[] { 3 } },
            new Billetera { Id = 4, UsuarioId = 4, SaldoTotal = 500, SaldoRetenido = 0, Version = new byte[] { 4 } }
        );

        modelBuilder.Entity<Categoria>().HasData(
            new Categoria { Id = 1, Nombre = "Tecnología", UrlIcono = "tech.png" },
            new Categoria { Id = 2, Nombre = "Coleccionables", UrlIcono = "col.png" },
            new Categoria { Id = 3, Nombre = "Indumentaria", UrlIcono = "ind.png" },
            new Categoria { Id = 4, Nombre = "Vehículos", UrlIcono = "veh.png" }
        );

        modelBuilder.Entity<Subasta>().HasData(
            new Subasta { Id = 1, VendedorId = 1, CategoriaId = 1, Titulo = "MacBook Pro", Descripcion = "Usada", UrlImagen = "https://images.unsplash.com/photo-1517336714731-489689fd1ca8?auto=format&fit=crop&w=600", PrecioBase = 30000, IncrementoMinimo = 5000, FechaInicio = activeStart, FechaFin = activeEnd, Estado = "ACTIVA", Version = new byte[] { 1 } },
            new Subasta { Id = 2, VendedorId = 1, CategoriaId = 2, Titulo = "Reloj Antiguo", Descripcion = "Colección", UrlImagen = "https://images.unsplash.com/photo-1524592094714-0f0654e20314?auto=format&fit=crop&w=600", PrecioBase = 10000, IncrementoMinimo = 1000, FechaInicio = activeStart, FechaFin = activeEnd, Estado = "ACTIVA", Version = new byte[] { 2 } },
            new Subasta { Id = 3, VendedorId = 1, CategoriaId = 3, Titulo = "Zapatillas Jordan", Descripcion = "Nuevas", UrlImagen = "https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&w=600", PrecioBase = 50000, IncrementoMinimo = 2000, FechaInicio = scheduledStart, FechaFin = scheduledEnd, Estado = "PROGRAMADA", Version = new byte[] { 3 } },
            new Subasta { Id = 4, VendedorId = 1, CategoriaId = 1, Titulo = "Monitor LG", Descripcion = "24 pulgadas", UrlImagen = "https://images.unsplash.com/photo-1527443224154-c4a3942d3acf?auto=format&fit=crop&w=600", PrecioBase = 15000, IncrementoMinimo = 1000, FechaInicio = seedDate.AddDays(-2), FechaFin = seedDate.AddDays(-1), Estado = "FINALIZADA", Version = new byte[] { 4 } },
            new Subasta { Id = 5, VendedorId = 1, CategoriaId = 4, Titulo = "Bicicleta", Descripcion = "Playera", UrlImagen = "https://images.unsplash.com/photo-1571068316344-75bc76f77890?auto=format&fit=crop&w=600", PrecioBase = 80000, IncrementoMinimo = 5000, FechaInicio = seedDate.AddDays(-3), FechaFin = seedDate.AddDays(-2), Estado = "DESIERTA", Version = new byte[] { 5 } }
        );

        modelBuilder.Entity<Puja>().HasData(
            new Puja { Id = 1, SubastaId = 1, CompradorId = 3, Monto = 35000, FechaPuja = activeStart.AddMinutes(10) },
            new Puja { Id = 2, SubastaId = 1, CompradorId = 2, Monto = 45000, FechaPuja = activeStart.AddMinutes(20) }
        );

        modelBuilder.Entity<TransaccionLedger>().HasData(
            new TransaccionLedger { Id = 1, BilleteraId = 2, Tipo = "DEPOSITO", Monto = 150000, Fecha = seedDate.AddDays(-1) },
            new TransaccionLedger { Id = 2, BilleteraId = 3, Tipo = "DEPOSITO", Monto = 200000, Fecha = seedDate.AddDays(-1) },
            new TransaccionLedger { Id = 3, BilleteraId = 4, Tipo = "DEPOSITO", Monto = 500, Fecha = seedDate.AddDays(-1) },
            new TransaccionLedger { Id = 4, BilleteraId = 2, Tipo = "RETENCION", Monto = 45000, Fecha = activeStart.AddMinutes(20), SubastaId = 1 }
        );
    }
}
