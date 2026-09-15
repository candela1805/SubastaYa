using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class AuctionClosingConcurrencyTests
{
    [Fact]
    public async Task Sqlite_rejects_a_stale_auction_version()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await using var firstContext = setup.CreateContext();
        await using var staleContext = setup.CreateContext();
        var firstAuction = await firstContext.Subastas.SingleAsync();
        var staleAuction = await staleContext.Subastas.SingleAsync();

        firstAuction.Titulo = "Cambio confirmado";
        await firstContext.SaveChangesAsync();

        staleAuction.Titulo = "Cambio con versión obsoleta";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_closers_only_settle_the_auction_once()
    {
        // InMemory se usa únicamente para coordinar dos escrituras sobre el
        // mismo token. Atomicidad, restricciones y rollback se prueban con
        // SQLite relacional en AuctionClosingServiceTests.
        var databaseRoot = new InMemoryDatabaseRoot();
        var interceptor = new ConcurrentClosingSaveInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                $"closing-concurrency-{Guid.NewGuid()}",
                databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(
                InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        var now = new DateTimeOffset(
            2026,
            9,
            15,
            22,
            0,
            0,
            TimeSpan.Zero);
        var auctionId = Guid.Parse(
            "81000000-0000-0000-0000-000000000001");
        var sellerId = Guid.Parse(
            "81000000-0000-0000-0000-000000000002");
        var buyerId = Guid.Parse(
            "81000000-0000-0000-0000-000000000003");

        await SeedAsync(
            options,
            now,
            auctionId,
            sellerId,
            buyerId);
        interceptor.Enabled = true;

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var clock = new FixedTimeProvider(now);
        var firstService = CreateService(firstContext, clock);
        var secondService = CreateService(secondContext, clock);

        var results = await Task.WhenAll(
            firstService.ProcessAsync(auctionId),
            secondService.ProcessAsync(auctionId));

        Assert.Single(
            results,
            result => result.Outcome == AuctionClosingOutcome.Finalized);
        Assert.Single(
            results,
            result => result.Outcome ==
                AuctionClosingOutcome.ConcurrencyConflict);

        await using var verification = new ApplicationDbContext(options);
        var buyerWallet = await verification.Billeteras.SingleAsync(
            wallet => wallet.UsuarioId == buyerId);
        var sellerWallet = await verification.Billeteras.SingleAsync(
            wallet => wallet.UsuarioId == sellerId);
        var settlement = await verification.LiquidacionesSubasta
            .SingleAsync();

        Assert.Equal(
            EstadoSubasta.Finalizada,
            (await verification.Subastas.SingleAsync()).Estado);
        Assert.Equal(870m, buyerWallet.SaldoTotal);
        Assert.Equal(0m, buyerWallet.SaldoRetenido);
        Assert.Equal(870m, buyerWallet.SaldoDisponible);
        Assert.Equal(630m, sellerWallet.SaldoTotal);
        Assert.Equal(630m, sellerWallet.SaldoDisponible);
        Assert.Equal(
            2,
            await verification.TransaccionLedgers.CountAsync(
                movement =>
                    movement.LiquidacionSubastaId == settlement.Id));
        Assert.Equal(
            1,
            await verification.AuditoriaLogs.CountAsync(
                audit => audit.TipoEvento == "AuctionSettled"));
    }

    private static AuctionClosingService CreateService(
        ApplicationDbContext context,
        TimeProvider clock)
    {
        return new AuctionClosingService(
            context,
            NoOpHubContext.Instance,
            clock,
            NullLogger<AuctionClosingService>.Instance);
    }

    private static async Task SeedAsync(
        DbContextOptions<ApplicationDbContext> options,
        DateTimeOffset now,
        Guid auctionId,
        Guid sellerId,
        Guid buyerId)
    {
        await using var context = new ApplicationDbContext(options);
        var seller = CreateUser(sellerId, "seller-concurrency@tests.local");
        var buyer = CreateUser(buyerId, "buyer-concurrency@tests.local");
        var sellerWalletId = Guid.Parse(
            "82000000-0000-0000-0000-000000000001");
        var buyerWalletId = Guid.Parse(
            "82000000-0000-0000-0000-000000000002");

        context.Usuarios.AddRange(seller, buyer);
        context.Billeteras.AddRange(
            new Billetera
            {
                Id = sellerWalletId,
                UsuarioId = sellerId,
                SaldoTotal = 500m,
                SaldoRetenido = 0m,
                SaldoDisponible = 500m
            },
            new Billetera
            {
                Id = buyerWalletId,
                UsuarioId = buyerId,
                SaldoTotal = 1_000m,
                SaldoRetenido = 130m,
                SaldoDisponible = 870m
            });
        context.Subastas.Add(new Subasta
        {
            Id = auctionId,
            VendedorId = sellerId,
            Titulo = "Subasta con cierre concurrente",
            Descripcion = "Prueba coordinada de optimistic locking.",
            Categoria = "Pruebas",
            PrecioInicial = 100m,
            PrecioActual = 130m,
            IncrementoMinimo = 10m,
            FechaInicioUtc = now.AddHours(-1),
            FechaFinUtc = now.AddMinutes(-1),
            Estado = EstadoSubasta.Activa
        });
        context.Pujas.Add(new Puja
        {
            Id = Guid.Parse(
                "83000000-0000-0000-0000-000000000001"),
            SubastaId = auctionId,
            UsuarioId = buyerId,
            Monto = 130m,
            EsGanadora = true,
            FechaUtc = now.AddMinutes(-2)
        });
        context.TransaccionLedgers.Add(new TransaccionLedger
        {
            Id = Guid.Parse(
                "84000000-0000-0000-0000-000000000001"),
            BilleteraId = buyerWalletId,
            Tipo = TipoMovimientoBilletera.Retencion,
            Monto = 130m,
            Descripcion = "Retención previa al cierre concurrente.",
            FechaUtc = now.AddMinutes(-2)
        });

        await context.SaveChangesAsync();
    }

    private static Usuario CreateUser(Guid id, string email)
    {
        return new Usuario
        {
            Id = id,
            Email = email,
            Nombre = email,
            PasswordHash = "TEST_ONLY",
            FechaRegistro = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class ConcurrentClosingSaveInterceptor :
        SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothClosersReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!Enabled ||
                eventData.Context is not ApplicationDbContext context ||
                !context.ChangeTracker.Entries<LiquidacionSubasta>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                return result;
            }

            var arrival = Interlocked.Increment(ref _arrivals);

            if (arrival == 2)
            {
                _bothClosersReady.TrySetResult();
            }

            await _bothClosersReady.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);

            if (arrival == 2)
            {
                throw new DbUpdateConcurrencyException(
                    "Conflicto optimista coordinado para la prueba.");
            }

            return result;
        }
    }
}
