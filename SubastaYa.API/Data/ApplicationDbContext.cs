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
                .IsRowVersion()
                .IsConcurrencyToken();
            
            
            entity.HasIndex(subasta => subasta.FechaFin);
            entity.HasIndex(subasta => subasta.Estado);
        });

        // Configuracion Fluent Api para billetera
        modelBuilder.Entity<Billetera>(entity =>
        {
            entity.Property(b => b.SaldoTotal).HasPrecision(18, 2);
            entity.Property(b => b.SaldoRetenido).HasPrecision(18,2);
            entity.Property(b => b.Version).IsRowVersion().IsConcurrencyToken();
        });
        
        modelBuilder.Entity<Puja>().Property(p => p.Monto).HasPrecision(18, 2);
        modelBuilder.Entity<TransaccionLedger>().Property(t => t.Monto).HasPrecision(18, 2);

        //Seed data 
        modelBuilder.Entity<Usuario>().HasData(
            new Usuario { Id = 1, Email = "vendedor@test.com", Nombre = "Vendedor", PasswordHash = "hash123", FechaRegistro = DateTime.UtcNow },
            new Usuario { Id = 2, Email = "comprador1@test.com", Nombre = "Comprador 1", PasswordHash = "hash123", FechaRegistro = DateTime.UtcNow },
            new Usuario { Id = 3, Email = "comprador2@test.com", Nombre = "Comprador 2", PasswordHash = "hash123", FechaRegistro = DateTime.UtcNow },
            new Usuario { Id = 4, Email = "sinfondos@test.com", Nombre = "Sin Fondos", PasswordHash = "hash123", FechaRegistro = DateTime.UtcNow }
        );
        modelBuilder.Entity<Billetera>().HasData(
            new Billetera { Id = 1, UsuarioId = 1, SaldoTotal = 0, SaldoRetenido = 0 },
            new Billetera { Id = 2, UsuarioId = 2, SaldoTotal = 150000, SaldoRetenido = 45000 },
            new Billetera { Id = 3, UsuarioId = 3, SaldoTotal = 200000, SaldoRetenido = 0 },
            new Billetera { Id = 4, UsuarioId = 4, SaldoTotal = 500, SaldoRetenido = 0 }
        );

        modelBuilder.Entity<Categoria>().HasData(
            new Categoria { Id = 1, Nombre = "Tecnología", UrlIcono = "tech.png" },
            new Categoria { Id = 2, Nombre = "Coleccionables", UrlIcono = "col.png" },
            new Categoria { Id = 3, Nombre = "Indumentaria", UrlIcono = "ind.png" },
            new Categoria { Id = 4, Nombre = "Vehículos", UrlIcono = "veh.png" }
        );

        var now = DateTime.UtcNow;
        modelBuilder.Entity<Subasta>().HasData(
            new Subasta { Id = 1, VendedorId = 1, CategoriaId = 1, Titulo = "MacBook Pro", Descripcion = "Usada", UrlImagen = "mac.jpg", PrecioBase = 30000, IncrementoMinimo = 5000, FechaInicio = now.AddMinutes(-30), FechaFin = now.AddMinutes(25), Estado = "ACTIVA" },
            new Subasta { Id = 2, VendedorId = 1, CategoriaId = 2, Titulo = "Reloj Antiguo", Descripcion = "Colección", UrlImagen = "reloj.jpg", PrecioBase = 10000, IncrementoMinimo = 1000, FechaInicio = now.AddMinutes(-60), FechaFin = now.AddMinutes(1), Estado = "ACTIVA" },
            new Subasta { Id = 3, VendedorId = 1, CategoriaId = 3, Titulo = "Zapatillas Jordan", Descripcion = "Nuevas", UrlImagen = "zapas.jpg", PrecioBase = 50000, IncrementoMinimo = 2000, FechaInicio = now.AddHours(24), FechaFin = now.AddHours(48), Estado = "PROGRAMADA" },
            new Subasta { Id = 4, VendedorId = 1, CategoriaId = 1, Titulo = "Monitor LG", Descripcion = "24 pulgadas", UrlImagen = "mon.jpg", PrecioBase = 15000, IncrementoMinimo = 1000, FechaInicio = now.AddDays(-2), FechaFin = now.AddDays(-1), Estado = "FINALIZADA" },
            new Subasta { Id = 5, VendedorId = 1, CategoriaId = 4, Titulo = "Bicicleta", Descripcion = "Playera", UrlImagen = "bici.jpg", PrecioBase = 80000, IncrementoMinimo = 5000, FechaInicio = now.AddDays(-3), FechaFin = now.AddDays(-2), Estado = "DESIERTA" }
        );

        modelBuilder.Entity<Puja>().HasData(
            new Puja { Id = 1, SubastaId = 1, CompradorId = 3, Monto = 35000, FechaPuja = now.AddMinutes(-20) },
            new Puja { Id = 2, SubastaId = 1, CompradorId = 2, Monto = 45000, FechaPuja = now.AddMinutes(-10) } 
        );

        modelBuilder.Entity<TransaccionLedger>().HasData(
            new TransaccionLedger { Id = 1, BilleteraId = 2, Tipo = "DEPOSITO", Monto = 150000, Fecha = now.AddDays(-1) },
            new TransaccionLedger { Id = 2, BilleteraId = 3, Tipo = "DEPOSITO", Monto = 200000, Fecha = now.AddDays(-1) },
            new TransaccionLedger { Id = 3, BilleteraId = 4, Tipo = "DEPOSITO", Monto = 500, Fecha = now.AddDays(-1) },
            new TransaccionLedger { Id = 4, BilleteraId = 2, Tipo = "RETENCION", Monto = 45000, Fecha = now.AddMinutes(-10), SubastaId = 1 }
        );
    }
}