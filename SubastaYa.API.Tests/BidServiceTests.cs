using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Contracts.Bids;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class BidServiceTests
{
    [Fact]
    public async Task First_valid_bid_freezes_balance_and_records_bid_ledger_and_audit()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var response = await database.CreateService(context).PlaceBidAsync(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderOneId,
            new PlaceBidRequest { Monto = 110m });

        await using var verification = database.CreateContext();
        var auction = await verification.Subastas.SingleAsync();
        var wallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        var bid = await verification.Pujas.SingleAsync();
        var ledger = await verification.TransaccionLedgers.SingleAsync();
        var audit = await verification.AuditoriaLogs.SingleAsync();

        Assert.Equal(110m, response.Monto);
        Assert.Equal("Liderando", response.Estado);
        Assert.False(response.SubastaExtendida);
        Assert.Equal(110m, auction.PrecioActual);
        Assert.Equal(110m, wallet.SaldoRetenido);
        Assert.Equal(890m, wallet.SaldoDisponible);
        Assert.Equal(1_000m, wallet.SaldoTotal);
        Assert.True(bid.EsGanadora);
        Assert.Equal(BidTestDatabase.BidderOneId, bid.UsuarioId);
        Assert.Equal(TipoMovimientoBilletera.Retencion, ledger.Tipo);
        Assert.Equal(110m, ledger.Monto);
        Assert.Contains(response.Id.ToString(), ledger.Descripcion);
        Assert.Equal("BidPlaced", audit.TipoEvento);
    }

    [Fact]
    public async Task Higher_bid_releases_previous_balance_and_freezes_new_balance_atomically()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await database.SeedLeadingBidAsync(
            BidTestDatabase.BidderOneId,
            110m);
        await using var context = database.CreateContext();

        var response = await database.CreateService(context).PlaceBidAsync(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderTwoId,
            new PlaceBidRequest { Monto = 130m });

        await using var verification = database.CreateContext();
        var previousWallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        var newWallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderTwoId);
        var bids = await verification.Pujas.ToListAsync();
        var ledgers = await verification.TransaccionLedgers.ToListAsync();

        Assert.Equal(0m, previousWallet.SaldoRetenido);
        Assert.Equal(1_000m, previousWallet.SaldoDisponible);
        Assert.Equal(130m, newWallet.SaldoRetenido);
        Assert.Equal(870m, newWallet.SaldoDisponible);
        Assert.Equal(2, bids.Count);
        Assert.Single(bids, bid => bid.EsGanadora);
        Assert.Equal(
            BidTestDatabase.BidderTwoId,
            bids.Single(bid => bid.EsGanadora).UsuarioId);
        Assert.Contains(
            ledgers,
            item => item.Tipo == TipoMovimientoBilletera.Liberacion &&
                item.Monto == 110m &&
                item.BilleteraId == BidTestDatabase.BidderOneWalletId);
        Assert.Contains(
            ledgers,
            item => item.Tipo == TipoMovimientoBilletera.Retencion &&
                item.Monto == 130m &&
                item.BilleteraId == BidTestDatabase.BidderTwoWalletId);
        Assert.Contains(response.Id.ToString(), ledgers.Single(
            item => item.Tipo == TipoMovimientoBilletera.Liberacion).Descripcion);
        Assert.Single(
            await verification.AuditoriaLogs.Where(
                item => item.TipoEvento == "BidPlaced").ToListAsync());
    }

    [Fact]
    public async Task Current_leader_only_freezes_the_difference()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await database.SeedLeadingBidAsync(
            BidTestDatabase.BidderOneId,
            110m);
        await using var context = database.CreateContext();

        await database.CreateService(context).PlaceBidAsync(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderOneId,
            new PlaceBidRequest { Monto = 130m });

        await using var verification = database.CreateContext();
        var wallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        var ledgers = await verification.TransaccionLedgers.ToListAsync();
        var bids = await verification.Pujas.ToListAsync();

        Assert.Equal(130m, wallet.SaldoRetenido);
        Assert.Equal(870m, wallet.SaldoDisponible);
        Assert.Contains(
            ledgers,
            item => item.Tipo == TipoMovimientoBilletera.Retencion &&
                item.Monto == 20m);
        Assert.DoesNotContain(
            ledgers,
            item => item.Tipo == TipoMovimientoBilletera.Liberacion);
        Assert.Equal(2, bids.Count);
        Assert.Single(bids, bid => bid.EsGanadora);
        Assert.Equal(130m, bids.Single(bid => bid.EsGanadora).Monto);
    }

    [Fact]
    public async Task Insufficient_balance_rejects_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync(
            bidderOneBalance: 109m);
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidInsufficientFundsException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 110m }));

        Assert.Equal("INSUFFICIENT_BALANCE", exception.Code);
        await AssertDatabaseUnchangedAsync(database, 109m);
    }

    [Fact]
    public async Task Bid_below_minimum_rejects_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidValidationException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 109.99m }));

        Assert.Equal("BID_BELOW_MINIMUM", exception.Code);
        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Null_zero_and_negative_amounts_are_rejected_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync();

        foreach (var amount in new decimal?[] { null, 0m, -1m })
        {
            await using var context = database.CreateContext();
            var exception = await Assert.ThrowsAsync<BidValidationException>(
                () => database.CreateService(context).PlaceBidAsync(
                    BidTestDatabase.AuctionId,
                    BidTestDatabase.BidderOneId,
                    new PlaceBidRequest { Monto = amount }));

            Assert.Equal("INVALID_BID_AMOUNT", exception.Code);
        }

        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Seller_cannot_bid_on_owned_auction()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidForbiddenException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.SellerId,
                new PlaceBidRequest { Monto = 110m }));

        Assert.Equal("OWN_AUCTION_BID", exception.Code);
        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Missing_auction_returns_not_found_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidNotFoundException>(
            () => database.CreateService(context).PlaceBidAsync(
                Guid.Parse("30000000-0000-0000-0000-000000000099"),
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 110m }));

        Assert.Equal("AUCTION_NOT_FOUND", exception.Code);
        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Finalized_auction_rejects_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync(
            auctionState: EstadoSubasta.Finalizada);
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidStateConflictException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 110m }));

        Assert.Equal("AUCTION_NOT_ACTIVE", exception.Code);
        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Active_but_expired_auction_rejects_without_side_effects()
    {
        await using var database = await BidTestDatabase.CreateAsync(
            remaining: TimeSpan.FromSeconds(-1));
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<BidStateConflictException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 110m }));

        Assert.Equal("AUCTION_ENDED", exception.Code);
        await AssertDatabaseUnchangedAsync(database);
    }

    [Theory]
    [InlineData(61, false)]
    [InlineData(60, true)]
    [InlineData(30, true)]
    public async Task Anti_sniping_uses_inclusive_sixty_second_boundary(
        int remainingSeconds,
        bool shouldExtend)
    {
        await using var database = await BidTestDatabase.CreateAsync(
            TimeSpan.FromSeconds(remainingSeconds));
        var originalEnd = database.Now.AddSeconds(remainingSeconds);
        await using var context = database.CreateContext();

        var response = await database.CreateService(context).PlaceBidAsync(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderOneId,
            new PlaceBidRequest { Monto = 110m });

        await using var verification = database.CreateContext();
        var auction = await verification.Subastas.SingleAsync();
        var extensionAudits = await verification.AuditoriaLogs.CountAsync(
            item => item.TipoEvento == "AuctionExtended");
        var expectedEnd = shouldExtend
            ? originalEnd.AddMinutes(2)
            : originalEnd;

        Assert.Equal(shouldExtend, response.SubastaExtendida);
        Assert.Equal(expectedEnd, response.FechaFinUtc);
        Assert.Equal(expectedEnd, auction.FechaFinUtc);
        Assert.Equal(shouldExtend ? 1 : 0, extensionAudits);
    }

    [Fact]
    public async Task Database_failure_rolls_back_bid_balances_ledger_and_audit()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER FailBidInsert
                BEFORE INSERT ON Pujas
                BEGIN
                    SELECT RAISE(ABORT, 'forced bid failure');
                END;
                """);
        }

        await using var context = database.CreateContext();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderOneId,
                new PlaceBidRequest { Monto = 110m }));

        await AssertDatabaseUnchangedAsync(database);
    }

    [Fact]
    public async Task Failure_after_releasing_leader_rolls_back_the_whole_bid()
    {
        await using var database = await BidTestDatabase.CreateAsync();
        await database.SeedLeadingBidAsync(
            BidTestDatabase.BidderOneId,
            110m);

        await using (var setup = database.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER FailReplacementBidInsert
                BEFORE INSERT ON Pujas
                BEGIN
                    SELECT RAISE(ABORT, 'forced replacement bid failure');
                END;
                """);
        }

        await using var context = database.CreateContext();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.CreateService(context).PlaceBidAsync(
                BidTestDatabase.AuctionId,
                BidTestDatabase.BidderTwoId,
                new PlaceBidRequest { Monto = 120m }));

        await using var verification = database.CreateContext();
        var auction = await verification.Subastas.SingleAsync();
        var originalLeader = await verification.Pujas.SingleAsync();
        var originalWallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);
        var challengerWallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderTwoId);
        var ledger = await verification.TransaccionLedgers.ToListAsync();

        Assert.Equal(110m, auction.PrecioActual);
        Assert.True(originalLeader.EsGanadora);
        Assert.Equal(BidTestDatabase.BidderOneId, originalLeader.UsuarioId);
        Assert.Equal(1_000m, originalWallet.SaldoTotal);
        Assert.Equal(110m, originalWallet.SaldoRetenido);
        Assert.Equal(890m, originalWallet.SaldoDisponible);
        Assert.Equal(1_000m, challengerWallet.SaldoTotal);
        Assert.Equal(0m, challengerWallet.SaldoRetenido);
        Assert.Equal(1_000m, challengerWallet.SaldoDisponible);
        Assert.Single(ledger);
        Assert.Equal(TipoMovimientoBilletera.Retencion, ledger[0].Tipo);
        Assert.Empty(await verification.AuditoriaLogs.ToListAsync());
    }

    private static async Task AssertDatabaseUnchangedAsync(
        BidTestDatabase database,
        decimal bidderOneBalance = 1_000m)
    {
        await using var verification = database.CreateContext();
        var auction = await verification.Subastas.SingleAsync();
        var wallet = await verification.Billeteras.SingleAsync(
            item => item.UsuarioId == BidTestDatabase.BidderOneId);

        Assert.Equal(100m, auction.PrecioActual);
        Assert.Equal(bidderOneBalance, wallet.SaldoTotal);
        Assert.Equal(0m, wallet.SaldoRetenido);
        Assert.Equal(bidderOneBalance, wallet.SaldoDisponible);
        Assert.Empty(await verification.Pujas.ToListAsync());
        Assert.Empty(await verification.TransaccionLedgers.ToListAsync());
        Assert.Empty(await verification.AuditoriaLogs.ToListAsync());
    }
}
