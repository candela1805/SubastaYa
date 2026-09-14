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
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Billetera> Billeteras => Set<Billetera>();
    public DbSet<TransaccionLedger> TransaccionLedgers => Set<TransaccionLedger>();
    public DbSet<Puja> Pujas => Set<Puja>();
    public DbSet<AuditoriaLog> AuditoriaLogs => Set<AuditoriaLog>();

    public override int SaveChanges()
    {
        ValidarAuditoriaAppendOnly();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        ValidarAuditoriaAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

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

            entity.Property(subasta => subasta.ImagenUrl)
                .HasMaxLength(500);

            entity.Property(subasta => subasta.Categoria)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(subasta => subasta.PrecioInicial)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.PrecioActual)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.IncrementoMinimo)
                .HasPrecision(18, 2);

            entity.Property(subasta => subasta.Estado)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(subasta => subasta.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(subasta => subasta.FechaFinUtc);
            entity.HasIndex(subasta => subasta.Estado);
            entity.HasIndex(subasta => subasta.Categoria);
            entity.HasIndex(subasta => subasta.PrecioActual);

            entity.HasOne(subasta => subasta.Vendedor)
                .WithMany()
                .HasForeignKey(subasta => subasta.VendedorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(subasta => subasta.VendedorId);
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("Usuarios");

            entity.HasKey(usuario => usuario.Id);

            entity.Property(usuario => usuario.Email)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(usuario => usuario.Nombre)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(usuario => usuario.PasswordHash)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(usuario => usuario.FechaRegistro)
                .IsRequired();

            entity.HasIndex(usuario => usuario.Email)
                .IsUnique();
        });

        modelBuilder.Entity<Billetera>(entity =>
        {
            entity.ToTable("Billeteras");

            entity.HasKey(billetera => billetera.Id);

            entity.Property(billetera => billetera.SaldoTotal)
                .HasPrecision(18, 2);

            entity.Property(billetera => billetera.SaldoRetenido)
                .HasPrecision(18, 2);

            entity.Property(billetera => billetera.SaldoDisponible)
                .HasPrecision(18, 2);

            entity.Property(billetera => billetera.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(billetera => billetera.UsuarioId)
                .IsUnique();

            entity.HasOne(billetera => billetera.Usuario)
                .WithOne(usuario => usuario.Billetera)
                .HasForeignKey<Billetera>(billetera => billetera.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Billeteras_SaldosNoNegativos",
                    "[SaldoTotal] >= 0 AND [SaldoRetenido] >= 0 AND [SaldoDisponible] >= 0");
                table.HasCheckConstraint(
                    "CK_Billeteras_RetenidoNoSuperaTotal",
                    "[SaldoRetenido] <= [SaldoTotal]");
                table.HasCheckConstraint(
                    "CK_Billeteras_SaldoDisponibleConsistente",
                    "[SaldoDisponible] = [SaldoTotal] - [SaldoRetenido]");
            });
        });

        modelBuilder.Entity<TransaccionLedger>(entity =>
        {
            entity.ToTable("TransaccionesLedger");

            entity.HasKey(transaccion => transaccion.Id);

            entity.Property(transaccion => transaccion.Tipo)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(transaccion => transaccion.Monto)
                .HasPrecision(18, 2);

            entity.Property(transaccion => transaccion.Descripcion)
                .IsRequired()
                .HasMaxLength(250);

            entity.Property(transaccion => transaccion.FechaUtc)
                .IsRequired();

            entity.HasOne(transaccion => transaccion.Billetera)
                .WithMany()
                .HasForeignKey(transaccion => transaccion.BilleteraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(transaccion => transaccion.BilleteraId);

            entity.HasIndex(transaccion => transaccion.FechaUtc);
        });

        modelBuilder.Entity<Puja>(entity =>
            {
                entity.HasKey(p => p.Id);

                entity.Property(p => p.Monto)
                .HasColumnType("decimal(18,2)");

                entity.HasIndex(p => new { p.SubastaId, p.FechaUtc });

                entity.HasIndex(p => new { p.SubastaId, p.EsGanadora });

                entity.HasOne(p => p.Subasta)
                    .WithMany()
                    .HasForeignKey(p => p.SubastaId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.Usuario)
                    .WithMany()
                    .HasForeignKey(p => p.UsuarioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        modelBuilder.Entity<AuditoriaLog>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.TipoEvento)
            .IsRequired()
            .HasMaxLength(100);

            entity.Property(a => a.Detalle)
            .IsRequired()
            .HasMaxLength(1000);

            entity.HasIndex(a => new { a.SubastaId, a.FechaUtc });

            entity.HasOne(a => a.Subasta)
            .WithMany()
            .HasForeignKey(a => a.SubastaId)
            .OnDelete(DeleteBehavior.Restrict);

        });
    }

    private void ValidarAuditoriaAppendOnly()
    {
        var auditoriaAlterada = ChangeTracker
            .Entries<AuditoriaLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (auditoriaAlterada)
        {
            throw new InvalidOperationException(
                "Los registros de auditoría son inmutables.");
        }
    }
}
