using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
    public DbSet<LiquidacionSubasta> LiquidacionesSubasta =>
        Set<LiquidacionSubasta>();

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidarEntidadesAppendOnly();
        ActualizarVersionesParaProveedoresSinRowVersion();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(
            acceptAllChangesOnSuccess: true,
            cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidarEntidadesAppendOnly();
        ActualizarVersionesParaProveedoresSinRowVersion();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
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

            ConfigurarTokenDeConcurrencia(
                entity.Property(subasta => subasta.Version));

            entity.HasIndex(subasta => subasta.FechaFinUtc);
            entity.HasIndex(subasta => subasta.Estado);
            entity.HasIndex(subasta => new
            {
                subasta.Estado,
                subasta.FechaFinUtc,
                subasta.Id
            })
                .HasDatabaseName("IX_Subastas_Estado_FechaFinUtc_Id");
            entity.HasIndex(subasta => new
            {
                subasta.Estado,
                subasta.FechaInicioUtc,
                subasta.Id
            })
                .HasDatabaseName("IX_Subastas_Estado_FechaInicioUtc_Id");
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

            ConfigurarTokenDeConcurrencia(
                entity.Property(billetera => billetera.Version));

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

            entity.Property(transaccion => transaccion.ClaveIdempotencia)
                .HasMaxLength(150);

            entity.HasOne(transaccion => transaccion.Billetera)
                .WithMany()
                .HasForeignKey(transaccion => transaccion.BilleteraId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(transaccion => transaccion.LiquidacionSubasta)
                .WithMany(liquidacion => liquidacion.Movimientos)
                .HasForeignKey(transaccion => transaccion.LiquidacionSubastaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(transaccion => transaccion.BilleteraId);

            entity.HasIndex(transaccion => transaccion.FechaUtc);

            entity.HasIndex(transaccion => new
            {
                transaccion.LiquidacionSubastaId,
                transaccion.Tipo
            })
                .HasDatabaseName(
                    "UX_TransaccionesLedger_LiquidacionSubastaId_Tipo")
                .IsUnique()
                .HasFilter("[LiquidacionSubastaId] IS NOT NULL");

            entity.HasIndex(transaccion => transaccion.ClaveIdempotencia)
                .HasDatabaseName(
                    "UX_TransaccionesLedger_ClaveIdempotencia")
                .IsUnique()
                .HasFilter("[ClaveIdempotencia] IS NOT NULL");

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_TransaccionesLedger_MontoPositivo",
                "[Monto] > 0"));
        });

        modelBuilder.Entity<LiquidacionSubasta>(entity =>
        {
            entity.ToTable("LiquidacionesSubasta");

            entity.HasKey(liquidacion => liquidacion.Id);

            entity.Property(liquidacion => liquidacion.ImporteFinal)
                .HasPrecision(18, 2);

            entity.Property(liquidacion => liquidacion.FechaAdjudicacionUtc)
                .IsRequired();

            entity.Property(liquidacion => liquidacion.FechaLiquidacionUtc)
                .IsRequired();

            entity.HasOne(liquidacion => liquidacion.Subasta)
                .WithOne(subasta => subasta.Liquidacion)
                .HasForeignKey<LiquidacionSubasta>(
                    liquidacion => liquidacion.SubastaId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(liquidacion => liquidacion.PujaGanadora)
                .WithMany()
                .HasForeignKey(liquidacion => liquidacion.PujaGanadoraId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(liquidacion => liquidacion.Comprador)
                .WithMany()
                .HasForeignKey(liquidacion => liquidacion.CompradorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(liquidacion => liquidacion.Vendedor)
                .WithMany()
                .HasForeignKey(liquidacion => liquidacion.VendedorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(liquidacion => liquidacion.SubastaId)
                .HasDatabaseName("UX_LiquidacionesSubasta_SubastaId")
                .IsUnique();

            entity.HasIndex(liquidacion => liquidacion.PujaGanadoraId)
                .HasDatabaseName(
                    "UX_LiquidacionesSubasta_PujaGanadoraId")
                .IsUnique();

            entity.HasIndex(liquidacion => liquidacion.CompradorId);

            entity.HasIndex(liquidacion => liquidacion.VendedorId);

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_LiquidacionesSubasta_ImporteFinalPositivo",
                "[ImporteFinal] > 0"));
        });

        modelBuilder.Entity<Puja>(entity =>
            {
                entity.HasKey(p => p.Id);

                entity.Property(p => p.Monto)
                .HasColumnType("decimal(18,2)");

                entity.HasIndex(p => new { p.SubastaId, p.FechaUtc });

                entity.HasIndex(p => p.SubastaId)
                    .HasDatabaseName("UX_Pujas_SubastaId_Ganadora")
                    .IsUnique()
                    .HasFilter("[EsGanadora] = 1");

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

            entity.Property(a => a.ClaveIdempotencia)
                .HasMaxLength(150);

            entity.HasIndex(a => new { a.SubastaId, a.FechaUtc });

            entity.HasIndex(a => a.ClaveIdempotencia)
                .HasDatabaseName("UX_AuditoriaLogs_ClaveIdempotencia")
                .IsUnique()
                .HasFilter("[ClaveIdempotencia] IS NOT NULL");

            entity.HasOne(a => a.Subasta)
            .WithMany()
            .HasForeignKey(a => a.SubastaId)
            .OnDelete(DeleteBehavior.Restrict);

        });
    }

    private void ValidarEntidadesAppendOnly()
    {
        var auditoriaAlterada = ChangeTracker
            .Entries<AuditoriaLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (auditoriaAlterada)
        {
            throw new InvalidOperationException(
                "Los registros de auditoría son inmutables.");
        }

        var ledgerAlterado = ChangeTracker
            .Entries<TransaccionLedger>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (ledgerAlterado)
        {
            throw new InvalidOperationException(
                "Los movimientos del Ledger son inmutables.");
        }

        var liquidacionAlterada = ChangeTracker
            .Entries<LiquidacionSubasta>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (liquidacionAlterada)
        {
            throw new InvalidOperationException(
                "Las liquidaciones de subasta son inmutables.");
        }
    }

    private void ConfigurarTokenDeConcurrencia(
        PropertyBuilder<byte[]> property)
    {
        if (Database.IsSqlServer())
        {
            property.IsRowVersion().IsConcurrencyToken();
            return;
        }

        property
            .IsConcurrencyToken()
            .ValueGeneratedNever();
    }

    private void ActualizarVersionesParaProveedoresSinRowVersion()
    {
        if (Database.IsSqlServer())
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<Subasta>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(subasta => subasta.Version).CurrentValue =
                Guid.NewGuid().ToByteArray();
        }

        foreach (var entry in ChangeTracker.Entries<Billetera>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(billetera => billetera.Version).CurrentValue =
                Guid.NewGuid().ToByteArray();
        }
    }
}
