using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Tests.TestInfrastructure;

namespace SubastaYa.API.Tests;

public sealed class AuctionClosingServiceTests
{
    [Fact]
    public async Task ProcessAsync_WithSeveralBids_AdjudicatesHighestWinner()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();

        await using (var actContext = setup.CreateContext())
        {
            var result = await setup.CreateService(actContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);

            Assert.Equal(AuctionClosingOutcome.Finalized, result.Outcome);
            Assert.NotNull(result.SettlementId);
        }

        await using var assertContext = setup.CreateContext();
        var auction = await assertContext.Subastas.SingleAsync();
        var settlement = await assertContext.LiquidacionesSubasta
            .SingleAsync();
        var bids = await assertContext.Pujas.ToListAsync();

        Assert.Equal(EstadoSubasta.Finalizada, auction.Estado);
        Assert.Equal(130m, auction.PrecioActual);
        Assert.Equal(AuctionClosingTestDatabase.AuctionId, settlement.SubastaId);
        Assert.Equal(AuctionClosingTestDatabase.WinningBidId, settlement.PujaGanadoraId);
        Assert.Equal(AuctionClosingTestDatabase.BuyerId, settlement.CompradorId);
        Assert.Equal(AuctionClosingTestDatabase.SellerId, settlement.VendedorId);
        Assert.Equal(130m, settlement.ImporteFinal);
        Assert.Equal(setup.Now, settlement.FechaAdjudicacionUtc);
        Assert.Equal(setup.Now, settlement.FechaLiquidacionUtc);
        Assert.Equal(2, bids.Count);
        Assert.Equal(
            130m,
            Assert.Single(bids, bid => bid.EsGanadora).Monto);
    }

    [Fact]
    public async Task ProcessAsync_SettlesEscrowAndCreatesCorrelatedEvidence()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync(
            winningAmount: 130m,
            unrelatedRetention: 50m);

        await using (var actContext = setup.CreateContext())
        {
            await setup.CreateService(actContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);
        }

        await using var assertContext = setup.CreateContext();
        var settlement = await assertContext.LiquidacionesSubasta
            .SingleAsync();
        var buyerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.BuyerWalletId);
        var sellerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.SellerWalletId);
        var movements = await assertContext.TransaccionLedgers
            .Where(movement =>
                movement.LiquidacionSubastaId == settlement.Id)
            .OrderBy(movement => movement.Tipo)
            .ToListAsync();
        var audit = await assertContext.AuditoriaLogs.SingleAsync(
            item => item.TipoEvento == "AuctionSettled");

        Assert.Equal(870m, buyerWallet.SaldoTotal);
        Assert.Equal(50m, buyerWallet.SaldoRetenido);
        Assert.Equal(820m, buyerWallet.SaldoDisponible);
        Assert.Equal(630m, sellerWallet.SaldoTotal);
        Assert.Equal(100m, sellerWallet.SaldoRetenido);
        Assert.Equal(530m, sellerWallet.SaldoDisponible);

        Assert.Equal(2, movements.Count);
        Assert.Contains(
            movements,
            movement =>
                movement.BilleteraId == buyerWallet.Id &&
                movement.Tipo == TipoMovimientoBilletera.Pago &&
                movement.Monto == 130m);
        Assert.Contains(
            movements,
            movement =>
                movement.BilleteraId == sellerWallet.Id &&
                movement.Tipo == TipoMovimientoBilletera.Cobro &&
                movement.Monto == 130m);
        Assert.All(
            movements,
            movement => Assert.False(
                string.IsNullOrWhiteSpace(movement.ClaveIdempotencia)));
        Assert.Equal(AuctionClosingTestDatabase.AuctionId, audit.SubastaId);
        Assert.Contains(settlement.Id.ToString(), audit.Detalle);
        Assert.False(string.IsNullOrWhiteSpace(audit.ClaveIdempotencia));
    }

    [Fact]
    public async Task ProcessAsync_WithoutBids_MarksDesertedWithoutMovingMoney()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();

        await using (var actContext = setup.CreateContext())
        {
            var result = await setup.CreateService(actContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);

            Assert.Equal(AuctionClosingOutcome.Deserted, result.Outcome);
            Assert.Null(result.SettlementId);
        }

        await using var assertContext = setup.CreateContext();
        var auction = await assertContext.Subastas.SingleAsync();
        var sellerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.SellerWalletId);
        var buyerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.BuyerWalletId);
        var audit = await assertContext.AuditoriaLogs.SingleAsync();

        Assert.Equal(EstadoSubasta.Desierta, auction.Estado);
        Assert.Empty(await assertContext.LiquidacionesSubasta.ToListAsync());
        Assert.Empty(await assertContext.TransaccionLedgers.ToListAsync());
        Assert.Equal(500m, sellerWallet.SaldoTotal);
        Assert.Equal(100m, sellerWallet.SaldoRetenido);
        Assert.Equal(400m, sellerWallet.SaldoDisponible);
        Assert.Equal(1_000m, buyerWallet.SaldoTotal);
        Assert.Equal(0m, buyerWallet.SaldoRetenido);
        Assert.Equal(1_000m, buyerWallet.SaldoDisponible);
        Assert.Equal("AuctionDeserted", audit.TipoEvento);
        Assert.Contains("vencimiento sin ofertas", audit.Detalle);
        Assert.False(string.IsNullOrWhiteSpace(audit.ClaveIdempotencia));
    }

    [Fact]
    public async Task ProcessAsync_AfterAntiSnipingExtension_WaitsForNewEnd()
    {
        await using var setup = await AuctionClosingTestDatabase.CreateAsync(
            remaining: TimeSpan.FromMinutes(2));
        await setup.SeedWinningBidAsync();

        await using (var beforeEndContext = setup.CreateContext())
        {
            var beforeEnd = await setup.CreateService(beforeEndContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);

            Assert.Equal(AuctionClosingOutcome.Skipped, beforeEnd.Outcome);
        }

        await using (var assertPendingContext = setup.CreateContext())
        {
            var auction = await assertPendingContext.Subastas.SingleAsync();
            Assert.Equal(EstadoSubasta.Activa, auction.Estado);
            Assert.Empty(
                await assertPendingContext.LiquidacionesSubasta.ToListAsync());
        }

        setup.Clock.SetUtcNow(setup.Now.AddMinutes(3));

        await using (var afterEndContext = setup.CreateContext())
        {
            var afterEnd = await setup.CreateService(afterEndContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);

            Assert.Equal(AuctionClosingOutcome.Finalized, afterEnd.Outcome);
        }

        await using var assertContext = setup.CreateContext();
        Assert.Equal(
            EstadoSubasta.Finalizada,
            (await assertContext.Subastas.SingleAsync()).Estado);
        Assert.Single(await assertContext.LiquidacionesSubasta.ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_WhenRepeated_IsIdempotent()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();

        await using (var firstContext = setup.CreateContext())
        {
            var first = await setup.CreateService(firstContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);
            Assert.Equal(AuctionClosingOutcome.Finalized, first.Outcome);
        }

        await using (var secondContext = setup.CreateContext())
        {
            var second = await setup.CreateService(secondContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);
            Assert.Equal(
                AuctionClosingOutcome.AlreadyProcessed,
                second.Outcome);
        }

        await using var assertContext = setup.CreateContext();
        var buyerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.BuyerWalletId);
        var sellerWallet = await assertContext.Billeteras.SingleAsync(
            wallet => wallet.Id == AuctionClosingTestDatabase.SellerWalletId);
        var settlement = await assertContext.LiquidacionesSubasta
            .SingleAsync();

        Assert.Equal(870m, buyerWallet.SaldoTotal);
        Assert.Equal(630m, sellerWallet.SaldoTotal);
        Assert.Equal(
            2,
            await assertContext.TransaccionLedgers.CountAsync(
                movement =>
                    movement.LiquidacionSubastaId == settlement.Id));
        Assert.Equal(
            1,
            await assertContext.AuditoriaLogs.CountAsync(
                audit => audit.TipoEvento == "AuctionSettled"));
    }

    [Fact]
    public async Task ProcessAsync_WhenSellerCreditFails_RollsBackAndCanRetry()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();

        await using (var triggerContext = setup.CreateContext())
        {
            await triggerContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER TR_FailSellerCredit
                BEFORE UPDATE ON Billeteras
                WHEN NEW.SaldoTotal > OLD.SaldoTotal
                BEGIN
                    SELECT RAISE(ABORT, 'forced seller credit failure');
                END;
                """);
        }

        await using (var failingContext = setup.CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => setup.CreateService(failingContext)
                    .ProcessAsync(AuctionClosingTestDatabase.AuctionId));
        }

        await using (var rollbackContext = setup.CreateContext())
        {
            var auction = await rollbackContext.Subastas.SingleAsync();
            var buyerWallet = await rollbackContext.Billeteras.SingleAsync(
                wallet => wallet.Id ==
                    AuctionClosingTestDatabase.BuyerWalletId);
            var sellerWallet = await rollbackContext.Billeteras.SingleAsync(
                wallet => wallet.Id ==
                    AuctionClosingTestDatabase.SellerWalletId);

            Assert.Equal(EstadoSubasta.Activa, auction.Estado);
            Assert.Equal(1_000m, buyerWallet.SaldoTotal);
            Assert.Equal(180m, buyerWallet.SaldoRetenido);
            Assert.Equal(820m, buyerWallet.SaldoDisponible);
            Assert.Equal(500m, sellerWallet.SaldoTotal);
            Assert.Equal(100m, sellerWallet.SaldoRetenido);
            Assert.Equal(400m, sellerWallet.SaldoDisponible);
            Assert.Empty(
                await rollbackContext.LiquidacionesSubasta.ToListAsync());
            Assert.Empty(
                await rollbackContext.AuditoriaLogs.Where(
                    audit => audit.TipoEvento == "AuctionSettled")
                    .ToListAsync());
            Assert.Empty(
                await rollbackContext.TransaccionLedgers.Where(
                    movement => movement.LiquidacionSubastaId != null)
                    .ToListAsync());

            await rollbackContext.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER TR_FailSellerCredit;");
        }

        await using (var retryContext = setup.CreateContext())
        {
            var retry = await setup.CreateService(retryContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);
            Assert.Equal(AuctionClosingOutcome.Finalized, retry.Outcome);
        }

        await using var assertContext = setup.CreateContext();
        Assert.Single(await assertContext.LiquidacionesSubasta.ToListAsync());
        Assert.Equal(
            2,
            await assertContext.TransaccionLedgers.CountAsync(
                movement => movement.LiquidacionSubastaId != null));
    }

    [Fact]
    public async Task ProcessAsync_WhenCancelled_DoesNotChangeState()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using (var actContext = setup.CreateContext())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => setup.CreateService(actContext).ProcessAsync(
                    AuctionClosingTestDatabase.AuctionId,
                    cancellation.Token));
        }

        await using var assertContext = setup.CreateContext();
        Assert.Equal(
            EstadoSubasta.Activa,
            (await assertContext.Subastas.SingleAsync()).Estado);
        Assert.Empty(await assertContext.LiquidacionesSubasta.ToListAsync());
        Assert.Empty(
            await assertContext.AuditoriaLogs.Where(
                audit => audit.TipoEvento == "AuctionSettled")
                .ToListAsync());
    }

    [Fact]
    public async Task SqliteSchema_EnforcesSingleWinningBid()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();

        await using var context = setup.CreateContext();
        context.Pujas.Add(new Puja
        {
            Id = Guid.NewGuid(),
            SubastaId = AuctionClosingTestDatabase.AuctionId,
            UsuarioId = AuctionClosingTestDatabase.OtherBidderId,
            Monto = 140m,
            EsGanadora = true,
            FechaUtc = setup.Now
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SqliteSchema_EnforcesLedgerIdempotencyKey()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        const string duplicateKey = "test:ledger:duplicate";

        await using (var firstContext = setup.CreateContext())
        {
            firstContext.TransaccionLedgers.Add(CreateLedger(
                duplicateKey,
                "Primer movimiento"));
            await firstContext.SaveChangesAsync();
        }

        await using var secondContext = setup.CreateContext();
        secondContext.TransaccionLedgers.Add(CreateLedger(
            duplicateKey,
            "Movimiento duplicado"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SqliteSchema_RejectsNonPositiveLedgerAmounts()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await using var context = setup.CreateContext();
        context.TransaccionLedgers.Add(new TransaccionLedger
        {
            Id = Guid.NewGuid(),
            BilleteraId = AuctionClosingTestDatabase.BuyerWalletId,
            Tipo = TipoMovimientoBilletera.Deposito,
            Monto = 0m,
            Descripcion = "Movimiento inválido de prueba.",
            FechaUtc = setup.Now
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SqliteSchema_RejectsNegativeWalletBalances()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await using var context = setup.CreateContext();
        var wallet = await context.Billeteras.SingleAsync(
            item => item.Id == AuctionClosingTestDatabase.BuyerWalletId);
        wallet.SaldoTotal = -1m;
        wallet.SaldoDisponible = -1m;

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SettlementEvidence_IsAppendOnly()
    {
        await using var setup =
            await AuctionClosingTestDatabase.CreateAsync();
        await setup.SeedWinningBidAsync();

        await using (var actContext = setup.CreateContext())
        {
            await setup.CreateService(actContext)
                .ProcessAsync(AuctionClosingTestDatabase.AuctionId);
        }

        await using (var settlementContext = setup.CreateContext())
        {
            var settlement = await settlementContext.LiquidacionesSubasta
                .SingleAsync();
            settlement.ImporteFinal += 1m;
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => settlementContext.SaveChangesAsync());
        }

        await using (var ledgerContext = setup.CreateContext())
        {
            var movement = await ledgerContext.TransaccionLedgers
                .FirstAsync(item => item.LiquidacionSubastaId != null);
            movement.Descripcion = "Alteración no permitida";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ledgerContext.SaveChangesAsync());
        }

        await using (var auditContext = setup.CreateContext())
        {
            var audit = await auditContext.AuditoriaLogs.SingleAsync();
            audit.Detalle = "Alteración no permitida";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => auditContext.SaveChangesAsync());
        }
    }

    private static TransaccionLedger CreateLedger(
        string key,
        string description)
    {
        return new TransaccionLedger
        {
            Id = Guid.NewGuid(),
            BilleteraId = AuctionClosingTestDatabase.BuyerWalletId,
            ClaveIdempotencia = key,
            Tipo = TipoMovimientoBilletera.Deposito,
            Monto = 1m,
            Descripcion = description,
            FechaUtc = DateTimeOffset.UtcNow
        };
    }
}
