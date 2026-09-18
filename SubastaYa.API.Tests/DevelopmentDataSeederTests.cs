using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SubastaYa.API.Data;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class DevelopmentDataSeederTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_run_creates_complete_consistent_demo_data()
    {
        await using var db = CreateContext();
        await Seed(db);

        Assert.Equal(5, await db.Usuarios.CountAsync());
        Assert.Equal(5, await db.Billeteras.CountAsync());
        Assert.Equal(5, await db.Subastas.CountAsync());
        Assert.Equal(3, await db.Pujas.CountAsync());
        Assert.Equal(8, await db.TransaccionLedgers.CountAsync());
        Assert.True(await db.Usuarios.AnyAsync(user =>
            user.Id == DevelopmentDataSeeder.DemoUserId));

        await AssertWallet(db, DevelopmentDataSeeder.BuyerOneId,
            150_000m, 45_000m, 105_000m);
        await AssertWallet(db, DevelopmentDataSeeder.BuyerTwoId,
            200_000m, 0m, 200_000m);
        await AssertWallet(db, DevelopmentDataSeeder.NoFundsId,
            500m, 0m, 500m);

        var standard = await db.Subastas.SingleAsync(item =>
            item.Id == DevelopmentDataSeeder.StandardAuctionId);
        var standardBids = await db.Pujas
            .Where(item => item.SubastaId == standard.Id)
            .OrderBy(item => item.FechaUtc)
            .ToListAsync();
        Assert.Equal(45_000m, standard.PrecioActual);
        Assert.Equal(2, standardBids.Count);
        Assert.Equal(DevelopmentDataSeeder.BuyerOneId,
            standardBids.Single(item => item.EsGanadora).UsuarioId);

        Assert.InRange(standard.FechaFinUtc - Now,
            TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30));
        var critical = await Auction(db, DevelopmentDataSeeder.CriticalAuctionId);
        Assert.InRange(critical.FechaFinUtc - Now,
            TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        var scheduled = await Auction(db, DevelopmentDataSeeder.ScheduledAuctionId);
        Assert.InRange(scheduled.FechaInicioUtc - Now,
            TimeSpan.FromHours(23), TimeSpan.FromHours(25));
        Assert.Equal(EstadoSubasta.Programada, scheduled.Estado);

        var expiredWinner = await Auction(
            db, DevelopmentDataSeeder.ExpiredWinnerAuctionId);
        var expiredDeserted = await Auction(
            db, DevelopmentDataSeeder.ExpiredDesertedAuctionId);
        Assert.True(expiredWinner.FechaFinUtc < Now);
        Assert.True(expiredDeserted.FechaFinUtc < Now);
        Assert.Equal(EstadoSubasta.Activa, expiredWinner.Estado);
        Assert.Equal(EstadoSubasta.Activa, expiredDeserted.Estado);
        Assert.Single(await db.Pujas.Where(item =>
            item.SubastaId == expiredWinner.Id).ToListAsync());
        Assert.False(await db.Pujas.AnyAsync(item =>
            item.SubastaId == expiredDeserted.Id));
    }

    [Fact]
    public async Task Second_run_is_idempotent_and_preserves_runtime_changes()
    {
        await using var db = CreateContext();
        await Seed(db);
        var standard = await Auction(db, DevelopmentDataSeeder.StandardAuctionId);
        var originalEnd = standard.FechaFinUtc;
        standard.FechaFinUtc = originalEnd.AddMinutes(2);
        standard.Estado = EstadoSubasta.Finalizada;
        var wallet = await db.Billeteras.SingleAsync(item =>
            item.UsuarioId == DevelopmentDataSeeder.BuyerOneId);
        wallet.SaldoTotal += 1_000m;
        wallet.SaldoDisponible += 1_000m;
        await db.SaveChangesAsync();

        var counts = await Counts(db);
        await Seed(db);

        Assert.Equal(counts, await Counts(db));
        Assert.Equal(originalEnd.AddMinutes(2), standard.FechaFinUtc);
        Assert.Equal(EstadoSubasta.Finalizada, standard.Estado);
        Assert.Equal(151_000m, wallet.SaldoTotal);
        Assert.Equal(106_000m, wallet.SaldoDisponible);
    }

    [Fact]
    public async Task Expired_scenarios_are_ready_for_real_closing_service()
    {
        await using var db = CreateContext();
        await Seed(db);
        var service = new AuctionClosingService(
            db,
            NoOpHubContext.Instance,
            new FixedTimeProvider(Now),
            NullLogger<AuctionClosingService>.Instance);

        var finalized = await service.ProcessAsync(
            DevelopmentDataSeeder.ExpiredWinnerAuctionId);
        var deserted = await service.ProcessAsync(
            DevelopmentDataSeeder.ExpiredDesertedAuctionId);

        Assert.Equal(AuctionClosingOutcome.Finalized, finalized.Outcome);
        Assert.Equal(AuctionClosingOutcome.Deserted, deserted.Outcome);
        Assert.True(await db.LiquidacionesSubasta.AnyAsync(item =>
            item.SubastaId == DevelopmentDataSeeder.ExpiredWinnerAuctionId &&
            item.CompradorId == DevelopmentDataSeeder.DemoUserId));
        Assert.Equal(
            EstadoSubasta.Desierta,
            (await Auction(
                db,
                DevelopmentDataSeeder.ExpiredDesertedAuctionId)).Estado);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seed-{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(
                InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Task Seed(ApplicationDbContext db) =>
        new DevelopmentDataSeeder(
            db,
            new PasswordHasher<Usuario>(),
            new FixedTimeProvider(Now))
        .SeedAsync("Usuario Demo", "demo@subastaya.com");

    private static async Task AssertWallet(
        ApplicationDbContext db, Guid userId, decimal total,
        decimal retained, decimal available)
    {
        var wallet = await db.Billeteras.SingleAsync(item =>
            item.UsuarioId == userId);
        Assert.Equal(total, wallet.SaldoTotal);
        Assert.Equal(retained, wallet.SaldoRetenido);
        Assert.Equal(available, wallet.SaldoDisponible);
    }

    private static Task<Subasta> Auction(
        ApplicationDbContext db, Guid id) =>
        db.Subastas.SingleAsync(item => item.Id == id);

    private static async Task<(int Users, int Wallets, int Auctions, int Bids,
        int Ledger, int Audits)> Counts(ApplicationDbContext db) => (
            await db.Usuarios.CountAsync(),
            await db.Billeteras.CountAsync(),
            await db.Subastas.CountAsync(),
            await db.Pujas.CountAsync(),
            await db.TransaccionLedgers.CountAsync(),
            await db.AuditoriaLogs.CountAsync());
}
