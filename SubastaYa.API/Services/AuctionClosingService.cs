using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SubastaYa.API.Data;
using SubastaYa.API.Hubs;
using SubastaYa.API.Models;

namespace SubastaYa.API.Services;

public sealed class AuctionClosingService : IAuctionClosingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuctionClosingService> _logger;

    public AuctionClosingService(
        ApplicationDbContext dbContext,
        IHubContext<AuctionHub> hubContext,
        TimeProvider timeProvider,
        ILogger<AuctionClosingService> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuctionClosingResult> ProcessAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        AuctionClosingResult result;
        DateTimeOffset closingTime;
        DateTimeOffset endDateUtc;

        try
        {
            closingTime = _timeProvider.GetUtcNow();
            var auction = await _dbContext.Subastas
                .SingleOrDefaultAsync(
                    item => item.Id == auctionId,
                    cancellationToken);

            if (auction is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AuctionClosingResult(
                    auctionId,
                    AuctionClosingOutcome.Skipped);
            }

            if (auction.Estado != EstadoSubasta.Activa)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AuctionClosingResult(
                    auctionId,
                    AuctionClosingOutcome.AlreadyProcessed);
            }

            if (auction.FechaFinUtc > closingTime)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AuctionClosingResult(
                    auctionId,
                    AuctionClosingOutcome.Skipped);
            }

            if (await _dbContext.LiquidacionesSubasta
                    .AsNoTracking()
                    .AnyAsync(
                        settlement => settlement.SubastaId == auctionId,
                        cancellationToken))
            {
                throw new InvalidOperationException(
                    $"La subasta {auctionId} sigue activa pero ya posee una liquidación.");
            }

            var winningBids = await _dbContext.Pujas
                .Where(bid =>
                    bid.SubastaId == auctionId &&
                    bid.EsGanadora)
                .OrderByDescending(bid => bid.Monto)
                .ThenBy(bid => bid.FechaUtc)
                .ThenBy(bid => bid.Id)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (winningBids.Count > 1)
            {
                throw new InvalidOperationException(
                    $"La subasta {auctionId} tiene más de una puja ganadora.");
            }

            if (winningBids.Count == 0)
            {
                var hasBids = await _dbContext.Pujas
                    .AsNoTracking()
                    .AnyAsync(
                        bid => bid.SubastaId == auctionId,
                        cancellationToken);

                if (hasBids)
                {
                    throw new InvalidOperationException(
                        $"La subasta {auctionId} tiene pujas pero ninguna ganadora.");
                }

                await transaction.RollbackAsync(cancellationToken);
                return new AuctionClosingResult(
                    auctionId,
                    AuctionClosingOutcome.NoBids);
            }

            var winningBid = winningBids[0];
            endDateUtc = auction.FechaFinUtc;
            var highestBidAmount = await _dbContext.Pujas
                .AsNoTracking()
                .Where(bid => bid.SubastaId == auctionId)
                .OrderByDescending(bid => bid.Monto)
                .Select(bid => bid.Monto)
                .FirstAsync(cancellationToken);

            if (winningBid.Monto != highestBidAmount ||
                winningBid.Monto != auction.PrecioActual ||
                winningBid.Monto <= 0)
            {
                throw new InvalidOperationException(
                    $"La puja ganadora de la subasta {auctionId} no coincide con su precio final.");
            }

            if (!auction.VendedorId.HasValue)
            {
                throw new InvalidOperationException(
                    $"La subasta {auctionId} no tiene vendedor asignado.");
            }

            var sellerId = auction.VendedorId.Value;

            if (sellerId == winningBid.UsuarioId)
            {
                throw new InvalidOperationException(
                    $"El vendedor de la subasta {auctionId} no puede ser su comprador.");
            }

            var buyerWallet = await _dbContext.Billeteras
                .SingleOrDefaultAsync(
                    wallet => wallet.UsuarioId == winningBid.UsuarioId,
                    cancellationToken);
            var sellerWallet = await _dbContext.Billeteras
                .SingleOrDefaultAsync(
                    wallet => wallet.UsuarioId == sellerId,
                    cancellationToken);

            if (buyerWallet is null)
            {
                throw new InvalidOperationException(
                    $"El comprador de la subasta {auctionId} no tiene billetera.");
            }

            if (sellerWallet is null)
            {
                throw new InvalidOperationException(
                    $"El vendedor de la subasta {auctionId} no tiene billetera.");
            }

            var amount = winningBid.Monto;

            if (buyerWallet.SaldoRetenido < amount ||
                buyerWallet.SaldoTotal < amount)
            {
                throw new InvalidOperationException(
                    $"La retención del comprador para la subasta {auctionId} es insuficiente.");
            }

            if (sellerWallet.SaldoTotal >
                    WalletService.MaxMoneyAmount - amount ||
                sellerWallet.SaldoDisponible >
                    WalletService.MaxMoneyAmount - amount)
            {
                throw new InvalidOperationException(
                    $"El crédito de la subasta {auctionId} supera el límite monetario del vendedor.");
            }

            var settlement = new LiquidacionSubasta
            {
                Id = Guid.NewGuid(),
                SubastaId = auction.Id,
                PujaGanadoraId = winningBid.Id,
                CompradorId = winningBid.UsuarioId,
                VendedorId = sellerId,
                ImporteFinal = amount,
                FechaAdjudicacionUtc = closingTime,
                FechaLiquidacionUtc = closingTime
            };

            buyerWallet.SaldoTotal -= amount;
            buyerWallet.SaldoRetenido -= amount;
            sellerWallet.SaldoTotal += amount;
            sellerWallet.SaldoDisponible += amount;
            auction.Estado = EstadoSubasta.Finalizada;

            _dbContext.LiquidacionesSubasta.Add(settlement);
            _dbContext.TransaccionLedgers.AddRange(
                new TransaccionLedger
                {
                    Id = Guid.NewGuid(),
                    BilleteraId = buyerWallet.Id,
                    LiquidacionSubastaId = settlement.Id,
                    Tipo = TipoMovimientoBilletera.Pago,
                    Monto = amount,
                    Descripcion =
                        $"Pago de la liquidación {settlement.Id} por la subasta {auctionId}.",
                    FechaUtc = closingTime
                },
                new TransaccionLedger
                {
                    Id = Guid.NewGuid(),
                    BilleteraId = sellerWallet.Id,
                    LiquidacionSubastaId = settlement.Id,
                    Tipo = TipoMovimientoBilletera.Cobro,
                    Monto = amount,
                    Descripcion =
                        $"Cobro de la liquidación {settlement.Id} por la subasta {auctionId}.",
                    FechaUtc = closingTime
                });
            _dbContext.AuditoriaLogs.Add(new AuditoriaLog
            {
                Id = Guid.NewGuid(),
                SubastaId = auctionId,
                TipoEvento = "AuctionSettled",
                Detalle =
                    $"Subasta adjudicada y liquidada. Liquidación: {settlement.Id}; puja: {winningBid.Id}; comprador: {winningBid.UsuarioId}; vendedor: {sellerId}; importe: {amount:F2}; origen: AuctionStateWorker.",
                FechaUtc = closingTime
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            result = new AuctionClosingResult(
                auctionId,
                AuctionClosingOutcome.Finalized,
                settlement.Id);
        }
        catch
        {
            await RollbackSafelyAsync(transaction);
            throw;
        }

        await NotifyStateChangedAsync(
            auctionId,
            EstadoSubasta.Finalizada,
            endDateUtc);

        return result;
    }

    private static async Task RollbackSafelyAsync(
        IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Se preserva la excepción original; DisposeAsync completa la limpieza.
        }
    }

    private async Task NotifyStateChangedAsync(
        Guid auctionId,
        EstadoSubasta state,
        DateTimeOffset endDateUtc)
    {
        try
        {
            await _hubContext.Clients
                .Group($"auction-{auctionId}")
                .SendAsync(
                    "AuctionStateChanged",
                    new
                    {
                        SubastaId = auctionId,
                        Estado = state.ToString(),
                        FechaFinUtc = endDateUtc
                    },
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "La subasta {AuctionId} fue liquidada, pero no pudo notificarse por SignalR.",
                auctionId);
        }
    }
}
